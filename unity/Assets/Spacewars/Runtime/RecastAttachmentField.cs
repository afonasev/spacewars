using System;
using System.Collections.Generic;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Restartable worker derivative. One field serves all endpoints on one side;
    // only the complete explicit branch is eligible for authority publication.
    internal sealed class RecastAttachmentField : NavOwnedStorage,IDisposable
    {
        internal enum Result { Pending,Succeeded,Failed }
        private enum Phase { Reset,WatchSetup,Seed,Pop,Up,Down,Watch,Neighbor,Done,Backtrack,Copy }
        private struct Watcher {internal int Next,Member;}
        private const long Header=512;
        private readonly NavLocalRepairWorkspace workspace;
        private readonly int[] positions,roots,heads,resolved;
        private readonly Watcher[] watchers;
        private readonly NavPoint[] targets,seeds;
        private readonly int[] seedStops,seedDonors;
        private readonly double cell;
        private NavOwnedText surface,retiredSurface;
        private NavPoint center,targetMinimum,targetMaximum;
        private double radius;
        private int members,seedCount,cursor,corner,watchCount,current,neighbor,watch,remainingMembers,heapIndex;
        private int routeMember,backtrack,pathCount,copy;
        private bool origin,disposed;
        private Phase phase,resume;
        internal Result Status{get;private set;}
        internal long Work{get;private set;}internal long Traces{get;private set;}internal long Expansions{get;private set;}
        internal int NodeCapacity=>workspace.Nodes.Length;
        internal long PaidLayout=>StorageBytes+workspace.StorageBytes;
        internal int Count=>workspace.RouteCount;
        internal NavLocation Location(int i)=>new NavLocation(workspace[i],new NavNameReference(surface,NavNameRole.Text));
        internal bool Resolved(int i)=>resolved[i]>=0;
        internal int SourceStop(int i)=>seedStops[roots[resolved[i]]];
        internal int SourceDonor(int i)=>seedDonors[roots[resolved[i]]];
        private static long Align(long n)=>checked((n+7)/8*8);
        private static long Layout(int v,int m,int s)=>checked(Header+9*32L+3*Align(4L*v)+Align(32L*m)+Align(16L*m)+Align(4L*m)+Align(16L*s)+2*Align(4L*s));
        private RecastAttachmentField(NavLocalRepairWorkspace workspace,double cell,int members,int seeds):base(Layout(workspace.Nodes.Length,members,seeds))
        {
            if(NavStorageLayout.ObjectBytesBound(typeof(RecastAttachmentField))>Header)throw new InvalidOperationException("Attachment field header estimate exceeded.");
            this.workspace=workspace;this.cell=cell;RequireEdge(workspace);workspace.Retain();
            phase=Phase.Done;Status=Result.Failed;
            int v=workspace.Nodes.Length;positions=new int[v];roots=new int[v];heads=new int[v];
            watchers=new Watcher[checked(4*members)];targets=new NavPoint[members];resolved=new int[members];this.seeds=new NavPoint[seeds];seedStops=new int[seeds];seedDonors=new int[seeds];
        }
        internal static RecastAttachmentField Create(double cell,double maximumRadius,int members,int seeds,NavOwnedStorage names,INavStorageReservation r)
        {
            if(cell<=0||maximumRadius<=0||members<1||seeds<1)throw new ArgumentException("Invalid attachment field dimensions.");
            int radius=checked((int)Math.Ceiling(maximumRadius/cell));
            var workspace=NavLocalRepairWorkspace.Create(radius,r.TryReserve,names);
            try{using(var allocation=new NavStorageAllocation(r.TryReserve)){
                allocation.Reserve(Layout(workspace.Nodes.Length,members,seeds));
                var result=allocation.Register(new RecastAttachmentField(workspace,cell,members,seeds));result.Retain();return result;
            }}finally{workspace.Release();}
        }
        internal void Begin(NavPoint center,double radius,string name,bool origin,int members,int seeds,INavStorageReservation r)
        {
            NavStorageAdmission.RequireOwned(r,this);
            if(disposed||phase!=Phase.Done||members<1||members>targets.Length||seeds<1||seeds>this.seeds.Length||radius<=0||Math.Ceiling(radius/cell)>workspace.Radius)throw new ArgumentException("Invalid attachment field reuse.");
            // Old node names remain live through incremental reset and cancellation.
            var nextSurface=NavOwnedText.Copy(name,r);RequireEdge(nextSurface);retiredSurface=surface;surface=nextSurface;
            this.center=center;this.radius=radius;this.origin=origin;this.members=members;seedCount=seeds;
            cursor=corner=watchCount=0;remainingMembers=members;workspace.OpenCount=workspace.RouteCount=0;Status=Result.Pending;phase=Phase.Reset;
#if RECAST_ATTACHMENT_PHASE_AUDIT
            global::RecastAttachmentPhaseAudit.FieldBegin(this,origin,center,radius,members,seeds,workspace.Nodes.Length,Work,Traces,Expansions,name);
#endif

        }
        // Callers count these bounded input copies in their preparation quantum.
        internal void SetTarget(int i,NavLocation location){if(i<0||i>=members||location.SurfaceId!=surface.Value)throw new ArgumentException("Wrong attachment endpoint.");targets[i]=location.Position;resolved[i]=-1;}
        internal void SetSeed(int i,int stop,NavLocation location,int donor=-1){if(i<0||i>=seedCount||location.SurfaceId!=surface.Value)throw new ArgumentException("Wrong attachment source.");seeds[i]=location.Position;seedStops[i]=stop;seedDonors[i]=donor;}
        private NavLocation At(NavPoint p)=>new NavLocation(p,new NavNameReference(surface,NavNameRole.Text));
        private NavPoint Point(int key){int x=key/workspace.Side-workspace.Radius,z=key%workspace.Side-workspace.Radius;return new NavPoint(center.X+x*cell,center.Z+z*cell);}
        private bool Inside(NavPoint p){double x=p.X-center.X,z=p.Z-center.Z;return x*x+z*z<=radius*radius+1e-9;}
        private int Corner(NavPoint p,int n){int x=checked((int)Math.Floor((p.X-center.X)/cell))+(n/2),z=checked((int)Math.Floor((p.Z-center.Z)/cell))+(n%2);return Math.Abs(x)>workspace.Radius||Math.Abs(z)>workspace.Radius?-1:workspace.Index(x,z);}
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        // Distance to the fixed box containing ALL targets is a consistent
        // potential for every distance edge. It stays fixed as targets resolve,
        // preserving closed/heap invariants without repeatedly favoring one
        // resolved endpoint. Stored costs remain g; physical tests are unchanged.
        private double Potential(int key){var p=Point(key);double x=Math.Max(targetMinimum.X-p.X,Math.Max(0,p.X-targetMaximum.X)),z=Math.Max(targetMinimum.Z-p.Z,Math.Max(0,p.Z-targetMaximum.Z));return Math.Sqrt(x*x+z*z);}
        private bool Less(int a,int b){double ca=workspace.Nodes[a].Cost+Potential(a),cb=workspace.Nodes[b].Cost+Potential(b);if(ca!=cb)return ca<cb;int sa=seedStops[roots[a]],sb=seedStops[roots[b]];return sa!=sb?sa<sb:a<b;}
        private void Swap(int a,int b){var open=workspace.Open;int v=open[a];open[a]=open[b];open[b]=v;positions[open[a]]=a;positions[open[b]]=b;}
        private void Queue(int key,Phase next){if(positions[key]<0){positions[key]=workspace.OpenCount;workspace.Open[workspace.OpenCount++]=key;}heapIndex=positions[key];resume=next;phase=Phase.Up;}
        private bool Better(int key,double cost,int source){var n=workspace.Nodes[key];return !n.Seen||cost<n.Cost||(cost==n.Cost&&seedStops[source]<seedStops[roots[key]]);}
#if RECAST_ATTACHMENT_PHASE_AUDIT
        private bool Whole(ILayeredNavigationProvider provider,NavGeometry geometry,double footprint,NavPoint from,NavPoint to,INavStorageReservation r){bool result=WholeCore(provider,geometry,footprint,from,to,r);global::RecastAttachmentPhaseAudit.FieldTrace(this,result);return result;}
        private bool WholeCore(ILayeredNavigationProvider provider,NavGeometry geometry,double footprint,NavPoint from,NavPoint to,INavStorageReservation r)
#else
        private bool Whole(ILayeredNavigationProvider provider,NavGeometry geometry,double footprint,NavPoint from,NavPoint to,INavStorageReservation r)
#endif
        {
            Traces++;if(!geometry.SegmentFree(from,to,footprint))return false;
            IReadOnlyList<NavTypedLeg> trace=null;var clearance=new NavClearanceProfile("ground",1,"ground",0,footprint);
            try{if(provider is PartitionNavigationProvider partition)return partition.TryTraceOwned(At(from),to,clearance,out var reached,out trace,r)&&reached.Equals(At(to));
                return RecastLayerTrace.Try(provider,At(from),At(to),clearance,out trace,r);
            }finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
        }
        internal Result Step(ILayeredNavigationProvider provider,NavGeometry geometry,double footprint,INavStorageReservation r,int maximumWork,Func<bool> cancelled=null)
        {
            NavStorageAdmission.RequireOwned(r,this);if(disposed)throw new ObjectDisposedException(nameof(RecastAttachmentField));
            int left=maximumWork;var nodes=workspace.Nodes;
            while(Status==Result.Pending||phase==Phase.Backtrack||phase==Phase.Copy){
                if(cancelled?.Invoke()==true)throw new OperationCanceledException();
                // Conservative charges include comparison and every heap move.
                int charge=phase==Phase.Down?8:phase==Phase.Up?6:phase==Phase.Reset||phase==Phase.Pop?4:2;
                if(left<charge)return Status;left-=charge;Work+=charge;
#if RECAST_ATTACHMENT_PHASE_AUDIT
                global::RecastAttachmentPhaseAudit.FieldCharge(this,(int)phase,charge);
#endif

                switch(phase){
                    case Phase.Reset:
                        nodes[cursor]=default;positions[cursor]=roots[cursor]=heads[cursor]=-1;cursor++;
                        if(cursor==nodes.Length){retiredSurface?.Release();retiredSurface=null;cursor=corner=0;phase=Phase.WatchSetup;}break;
                    case Phase.WatchSetup:{
                        // One bounded update in the existing paid target setup;
                        // input copy order is unrestricted and no heap exists yet.
                        if(corner==0){var p=targets[cursor];if(cursor==0){targetMinimum=targetMaximum=p;}else{targetMinimum=new NavPoint(Math.Min(targetMinimum.X,p.X),Math.Min(targetMinimum.Z,p.Z));targetMaximum=new NavPoint(Math.Max(targetMaximum.X,p.X),Math.Max(targetMaximum.Z,p.Z));}}
                        int key=Corner(targets[cursor],corner++);
                        if(key>=0&&Inside(Point(key))){watchers[watchCount]=new Watcher{Next=heads[key],Member=cursor};heads[key]=watchCount++;}
                        if(corner==4){corner=0;if(++cursor==members){cursor=0;phase=Phase.Seed;}}break;
                    }
                    case Phase.Seed:{
                        if(cursor==seedCount){phase=Phase.Pop;break;}
                        int source=cursor,key=Corner(seeds[source],corner++);if(corner==4){corner=0;cursor++;}
                        if(key<0||!Inside(seeds[source])||!Inside(Point(key)))break;
                        double cost=Distance(seeds[source],Point(key));if(!Better(key,cost,source))break;
                        if(!Whole(provider,geometry,footprint,origin?Point(key):seeds[source],origin?seeds[source]:Point(key),r))break;
                        nodes[key]=new NavLocalRepairWorkspace.Node{Seen=true,Location=At(Point(key)),Cost=cost,Parent=-1};roots[key]=source;Queue(key,Phase.Seed);break;
                    }
                    case Phase.Up:
                        if(heapIndex==0){phase=resume;break;}
                        int parent=(heapIndex-1)/2;if(!Less(workspace.Open[heapIndex],workspace.Open[parent])){phase=resume;break;}Swap(heapIndex,parent);heapIndex=parent;break;
                    case Phase.Down:{
                        int a=2*heapIndex+1;if(a>=workspace.OpenCount){phase=Phase.Watch;break;}int b=a+1;if(b<workspace.OpenCount&&Less(workspace.Open[b],workspace.Open[a]))a=b;
                        if(!Less(workspace.Open[a],workspace.Open[heapIndex])){phase=Phase.Watch;break;}Swap(a,heapIndex);heapIndex=a;break;
                    }
                    case Phase.Pop:
                        if(remainingMembers==0){
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.FieldEnd(this,"all-resolved",remainingMembers,workspace.OpenCount,resolved,Work,Traces,Expansions);
#endif
Status=Result.Succeeded;phase=Phase.Done;break;}
                        if(workspace.OpenCount==0){
#if RECAST_ATTACHMENT_PHASE_AUDIT
global::RecastAttachmentPhaseAudit.FieldEnd(this,"open-exhausted",remainingMembers,workspace.OpenCount,resolved,Work,Traces,Expansions);
#endif
Status=Result.Failed;phase=Phase.Done;break;}
                        current=workspace.Open[0];positions[current]=-1;workspace.OpenCount--;
                        if(workspace.OpenCount>0){workspace.Open[0]=workspace.Open[workspace.OpenCount];positions[workspace.Open[0]]=0;}
                        nodes[current].Closed=true;Expansions++;watch=heads[current];neighbor=0;heapIndex=0;phase=workspace.OpenCount>0?Phase.Down:Phase.Watch;break;
                    case Phase.Watch:{
                        if(watch<0){phase=Phase.Neighbor;break;}var w=watchers[watch];watch=w.Next;
                        if(resolved[w.Member]>=0)break;
                        if(Whole(provider,geometry,footprint,origin?targets[w.Member]:Point(current),origin?Point(current):targets[w.Member],r)){resolved[w.Member]=current;remainingMembers--;}
                        break;
                    }
                    case Phase.Neighbor:{
                        if(neighbor==9){phase=Phase.Pop;break;}int x=current/workspace.Side-workspace.Radius+neighbor/3-1,z=current%workspace.Side-workspace.Radius+neighbor%3-1;neighbor++;
                        if(Math.Abs(x)>workspace.Radius||Math.Abs(z)>workspace.Radius)break;int key=workspace.Index(x,z);if(key==current||nodes[key].Closed||!Inside(Point(key)))break;
                        double cost=nodes[current].Cost+Distance(Point(current),Point(key));int source=roots[current];if(!Better(key,cost,source))break;
                        if(!Whole(provider,geometry,footprint,origin?Point(key):Point(current),origin?Point(current):Point(key),r))break;
                        nodes[key]=new NavLocalRepairWorkspace.Node{Seen=true,Location=At(Point(key)),Cost=cost,Parent=current};roots[key]=source;Queue(key,Phase.Neighbor);break;
                    }
                    case Phase.Backtrack:
                        if(backtrack<0){copy=0;phase=Phase.Copy;break;}
                        if(pathCount+3>4096)throw new InvalidOperationException("Recast branch waypoint capacity exceeded.");
                        workspace.Reversed[pathCount++]=Point(backtrack);backtrack=nodes[backtrack].Parent;break;
                    case Phase.Copy:
                        if(copy==0)workspace.Smoothed[copy++]=origin?targets[routeMember]:seeds[roots[resolved[routeMember]]];
                        else if(copy<=pathCount){workspace.Smoothed[copy]=workspace.Reversed[origin?copy-1:pathCount-copy];copy++;}
                        else{workspace.Smoothed[copy]=origin?seeds[roots[resolved[routeMember]]]:targets[routeMember];workspace.RouteCount=copy+1;phase=Phase.Done;}break;
                    default:throw new InvalidOperationException("Invalid attachment field phase.");
                }
            }
            return Status;
        }
        internal void BeginRoute(int member)
        {
            if(phase!=Phase.Done||member<0||member>=members||resolved[member]<0)throw new InvalidOperationException("Attachment route is incomplete.");
            routeMember=member;backtrack=resolved[member];pathCount=copy=0;workspace.RouteCount=0;phase=Phase.Backtrack;
        }
        internal bool RoutePending=>phase==Phase.Backtrack||phase==Phase.Copy;
        public void Dispose(){if(disposed)return;disposed=true;Release();}
        protected override void ReleaseEdges(){workspace.Release();surface?.Release();surface=null;retiredSurface?.Release();retiredSurface=null;}
    }
}
