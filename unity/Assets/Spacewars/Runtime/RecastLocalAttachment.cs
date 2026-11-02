using System;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Worker-only bounded connector. It cannot publish a partial route or retain
    // a preparation reservation. Accepted turns are copied into the route body.
    internal sealed class RecastLocalAttachment : NavOwnedStorage,IDisposable
    {
        internal enum Result { Pending,Succeeded,Failed }
        private enum Phase { Reset,Select,Shift,Target,Neighbors,Backtrack,Copy,Done }
        private readonly NavLocalRepairWorkspace workspace;
        private readonly double cell;
        private NavOwnedText surface,targetSurface;
        private readonly NavLocation[] typedReversed,typedRoute;
        private bool crossSurface;
        private NavLocation root,target;
        private bool reverse,disposed;
        private Phase phase;
        private int reset,scan,best,shift,current,neighbor,backtrack,copy,pathCount;
        internal Result Status{get;private set;}
        internal long Work{get;private set;}internal long Traces{get;private set;}internal long Expansions{get;private set;}
        internal int Count=>Status==Result.Succeeded?workspace.RouteCount:0;
        internal int NodeCapacity=>workspace.Nodes.Length;
        internal NavLocation Location(int index)=>typedRoute[index];
        private static long Layout(NavLocalRepairWorkspace workspace)=>checked(NavStorageLayout.ObjectBytesBound(typeof(RecastLocalAttachment))+64L+2L*workspace.Nodes.Length*NavStorageLayout.ValueBytes(typeof(NavLocation)));
        private RecastLocalAttachment(NavLocalRepairWorkspace workspace,double cell):base(Layout(workspace))
        {this.workspace=workspace;this.cell=cell;typedReversed=new NavLocation[workspace.Nodes.Length];typedRoute=new NavLocation[workspace.Nodes.Length];RequireEdge(workspace);workspace.Retain();}
        internal static RecastLocalAttachment Create(NavigationProfile profile,NavOwnedStorage names,INavStorageReservation reservation)
        {
            int radius=checked((int)Math.Ceiling(profile.LocalDetourDistance/profile.LocalGridCell));
            var workspace=NavLocalRepairWorkspace.Create(radius,reservation.TryReserve,names);
            try{using(var allocation=new NavStorageAllocation(reservation.TryReserve)){
                allocation.Reserve(Layout(workspace));
                var result=allocation.Register(new RecastLocalAttachment(workspace,profile.LocalGridCell));result.Retain();return result;
            }}finally{workspace.Release();}
        }
        // Reverse search validates next->current and target->current. It never
        // treats reverse reachability as permission to traverse a directed edge.
        internal void Begin(NavLocation from,NavLocation to,bool reverse,INavStorageReservation reservation)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            if(disposed)throw new ObjectDisposedException(nameof(RecastLocalAttachment));
            surface?.Release();surface=null;targetSurface?.Release();targetSurface=null;workspace.OpenCount=workspace.RouteCount=0;
            Status=Result.Failed;phase=Phase.Done;
            crossSurface=from.SurfaceId!=to.SurfaceId;if(crossSurface&&reverse)return;
            surface=NavOwnedText.Copy(from.SurfaceId,reservation);RequireEdge(surface);
            var name=new NavNameReference(surface,NavNameRole.Text);
            root=new NavLocation(from.Position,name);if(crossSurface){targetSurface=NavOwnedText.Copy(to.SurfaceId,reservation);RequireEdge(targetSurface);}target=new NavLocation(to.Position,crossSurface?new NavNameReference(targetSurface,NavNameRole.Text):name);this.reverse=reverse;
            reset=scan=best=shift=neighbor=copy=pathCount=0;current=-1;Status=Result.Pending;phase=Phase.Reset;
        }
        internal Result Step(ILayeredNavigationProvider provider,NavGeometry geometry,NavigationProfile profile,INavStorageReservation reservation,int maximumWork,Func<bool> cancelled=null)
        {
            NavStorageAdmission.RequireOwned(reservation,this);
            if(disposed)throw new ObjectDisposedException(nameof(RecastLocalAttachment));
            if(profile.LocalGridCell!=cell||Math.Ceiling(profile.LocalDetourDistance/cell)!=workspace.Radius)throw new ArgumentException("Local connector profile changed.");
            if(crossSurface&&!(provider is PartitionNavigationProvider))throw new ArgumentException("Cross-support connector requires its exact physical partition.");
            int remaining=maximumWork;
            var nodes=workspace.Nodes;var open=workspace.Open;
            while(Status==Result.Pending){
                if(cancelled?.Invoke()==true)throw new OperationCanceledException();
                if(remaining<=0)return Status;
                switch(phase){
                    case Phase.Reset:
                        nodes[reset++]=default;remaining--;Work++;
                        if(reset==nodes.Length){int start=workspace.Index(0,0);nodes[start]=new NavLocalRepairWorkspace.Node{Seen=true,Location=root,Parent=-1};open[workspace.OpenCount++]=start;scan=best=0;phase=Phase.Select;}
                        break;
                    case Phase.Select:
                        if(workspace.OpenCount==0){Status=Result.Failed;phase=Phase.Done;break;}
                        if(scan<workspace.OpenCount){
                            var a=nodes[open[scan]];var b=nodes[open[best]];
                            if(a.Score<b.Score-1e-9||(Math.Abs(a.Score-b.Score)<=1e-9&&a.Side<b.Side))best=scan;
                            scan++;remaining--;Work++;break;
                        }
                        current=open[best];shift=best;phase=Phase.Shift;break;
                    case Phase.Shift:
                        if(shift+1<workspace.OpenCount){open[shift]=open[shift+1];shift++;remaining--;Work++;break;}
                        workspace.OpenCount--;nodes[current].Closed=true;Expansions++;phase=Phase.Target;break;
                    case Phase.Target:
                        remaining--;Work++;Traces++;
                        if(Whole(provider,geometry,profile.Radius,reverse?target:nodes[current].Location,reverse?nodes[current].Location:target,reservation)){
                            backtrack=current;pathCount=0;phase=Phase.Backtrack;
                        }else{neighbor=0;phase=Phase.Neighbors;}
                        break;
                    case Phase.Neighbors:
                        if(neighbor==9){scan=best=0;phase=Phase.Select;break;}
                        // A neighbor examination and its possible wholeTrace are
                        // two units. Do not partially consume this pair on yield.
                        if(remaining<2)return Status;
                        int ox=neighbor/3-1,oz=neighbor%3-1;neighbor++;remaining--;Work++;
                        if(ox==0&&oz==0)break;
                        var row=nodes[current];int x=row.X+ox,z=row.Z+oz;
                        if(x*x+z*z>workspace.Radius*workspace.Radius)break;
                        int key=workspace.Index(x,z);if(nodes[key].Closed)break;
                        var point=new NavPoint(root.Position.X+x*cell,root.Position.Z+z*cell);
                        double cost=row.Cost+Distance(row.Location.Position,point);
                        if(nodes[key].Seen&&cost>=nodes[key].Cost)break;
                        var location=new NavLocation(point,new NavNameReference(surface,NavNameRole.Text));
                        remaining--;Work++;Traces++;
                        if(crossSurface){if(!AdvancePartition((PartitionNavigationProvider)provider,geometry,profile.Radius,row.Location,point,reservation,out location))break;}
                        else if(!Whole(provider,geometry,profile.Radius,reverse?location:row.Location,reverse?row.Location:location,reservation))break;
                        if(!nodes[key].Seen)open[workspace.OpenCount++]=key;
                        nodes[key]=new NavLocalRepairWorkspace.Node{Seen=true,Location=location,X=x,Z=z,Cost=cost,Score=cost+Distance(point,target.Position),Side=(target.Position.X-root.Position.X)*z-(target.Position.Z-root.Position.Z)*x,Parent=current};
                        break;
                    case Phase.Backtrack:
                        if(nodes[backtrack].Parent<0){copy=0;phase=Phase.Copy;break;}
                        typedReversed[pathCount++]=nodes[backtrack].Location;backtrack=nodes[backtrack].Parent;remaining--;Work++;break;
                    case Phase.Copy:
                        if(copy<pathCount){typedRoute[copy]=typedReversed[reverse?copy:pathCount-copy-1];copy++;remaining--;Work++;break;}
                        typedRoute[pathCount]=reverse?root:target;workspace.RouteCount=pathCount+1;remaining--;Work++;Status=Result.Succeeded;phase=Phase.Done;break;
                    default:throw new InvalidOperationException("Invalid local connector phase.");
                }
            }
            return Status;
        }
        // Partition traversal selects the actual support at each new grid point.
        // Every hop retains its complete physical trace; XZ proximity never
        // authorizes a portal, height discontinuity or unsupported location.
        private static bool AdvancePartition(PartitionNavigationProvider provider,NavGeometry geometry,double radius,NavLocation from,NavPoint to,INavStorageReservation r,out NavLocation reached)
        {
            reached=default;if(!geometry.SegmentFree(from.Position,to,radius))return false;
            System.Collections.Generic.IReadOnlyList<NavTypedLeg> trace=null;
            try{return provider.TryTraceOwned(from,to,new NavClearanceProfile("ground",1,"ground",0,radius),out reached,out trace,r)&&reached.Position.Equals(to);}
            finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
        }
        private static bool Whole(ILayeredNavigationProvider provider,NavGeometry geometry,double radius,NavLocation from,NavLocation to,INavStorageReservation reservation)
        {
            if(!geometry.SegmentFree(from.Position,to.Position,radius))return false;
            var clearance=new NavClearanceProfile("ground",1,"ground",0,radius);
            System.Collections.Generic.IReadOnlyList<NavTypedLeg> trace=null;
            try{if(provider is PartitionNavigationProvider partition)return partition.TryTraceOwned(from,to.Position,clearance,out var reached,out trace,reservation)&&reached.Equals(to);
                return RecastLayerTrace.Try(provider,from,to,clearance,out trace,reservation);
            }finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
        }
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        public void Dispose(){if(disposed)return;disposed=true;Release();}
        protected override void ReleaseEdges(){surface?.Release();surface=null;targetSurface?.Release();targetSurface=null;workspace.Release();}
    }
}
