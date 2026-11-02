using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    // Worker-only derivative state. Pending requests remain in the authority mailbox,
    // so capture/load may restart this work without persisting native pointers.
    internal sealed class RecastGroupPreparation:IDisposable
    {
        private sealed class Context:NavOwnedStorage
        {
            internal Context Next;internal NavigationAdmissionPin Pin;
            internal NavigationRequest[] Members;internal RecastMemberRoute[] Routes;
            internal RecastMapCache.Pin Map;internal RecastNavigationQuery Query;
            internal RecastPayload ResultPayload;internal ILayeredNavigationProvider Provider;
            internal RecastPayload RawCorridor,CompletedCorridor;internal RecastSpinePreparation SpinePreparation;internal bool SpineRequired;
            internal NavSolveStatus Result=NavSolveStatus.Pending;
            private Context(long bytes):base(bytes){}
            internal static Context Create(NavigationAdmissionPin pin,NavigationRequest request,INavStorageReservation r){
                int count=0;foreach(var member in pin.Value.Requests)if(Compatible(request,member))count++;
                if(count==0)throw new InvalidOperationException("Unpublished Recast subgroup.");
                long bytes=checked(NavStorageLayout.ObjectBytesBound(typeof(Context))+32+8L*count);
                if(!r.TryReserve(NavStorageLayout.Charge(bytes)))throw new RecastNative.CapacityRefused();
                var result=new Context(bytes);
                try{
                    if(!(r is INavStorageLocalBirthAllocator allocator)||!allocator.TryRegisterLocalBirth(result))throw new InvalidOperationException("Recast context requires physical ownership.");
                    if(pin.StorageOwner!=null)result.RequireEdge(pin.StorageOwner);result.Pin=new NavigationAdmissionPin(pin.StorageOwner,pin.Value);result.Members=new NavigationRequest[count];int n=0;
                    foreach(var member in pin.Value.Requests)if(Compatible(request,member))result.Members[n++]=member;
                    Array.Sort(result.Members,(a,b)=>a.Request.CompareTo(b.Request));return result;
                }catch{result.Release();throw;}
            }
            internal int Index(NavigationRequest request){for(int i=0;i<Members.Length;i++)if(ReferenceEquals(Members[i],request))return i;return -1;}
            protected override void ReleaseEdges(){Query?.Dispose();Query=null;SpinePreparation?.Dispose();SpinePreparation=null;CompletedCorridor?.Dispose();CompletedCorridor=null;RawCorridor?.Dispose();RawCorridor=null;Map.Dispose();(Provider as INavStorageFacade)?.StorageRoot.Release();Provider=null;ResultPayload?.Dispose();ResultPayload=null;Routes=null;Members=null;Pin.Dispose();Pin=default;Next=null;}
        }
        internal int QueryStepWork=256;
        private Context first;
        private readonly RecastMapCache cache=new RecastMapCache();
        private RecastPayload owner;
        private bool disposed;
        private RecastGroupPreparation(RecastPayload owner){this.owner=owner;owner.Value=this;}
        internal static RecastGroupPreparation Create(INavStorageReservation r){var payload=RecastPayload.Allocate(r,NavStorageLayout.ObjectBytesBound(typeof(RecastGroupPreparation))+NavStorageLayout.ObjectBytesBound(typeof(RecastMapCache))+NavStorageLayout.ObjectBytesBound(typeof(object)));try{return new RecastGroupPreparation(payload);}catch{payload.Dispose();throw;}}
        internal void AdmitOwner(INavStorageReservation r)=>NavStorageAdmission.RequireOwned(r,owner);
        internal long ColdBuilds=>cache.ColdBuilds;internal long WarmHits=>cache.WarmHits;
        internal long FoundationBuilds=>cache.FoundationBuilds;internal long FoundationReuses=>cache.FoundationReuses;
        internal long PrepareCount=>cache.PrepareCount;internal long GeometryDigests=>cache.GeometryDigests;
        internal bool HasTransientWork=>first!=null;
        internal void PreparePublic(IPlayableTerrain terrain,NavigationProfile profile,INavStorageReservation r,Func<bool> cancelled){AdmitOwner(r);cache.PreparePublic(terrain,profile,r,cancelled);}
        internal void PreparePublic(IPlayableTerrain terrain,NavGeometry geometry,NavigationProfile profile,INavStorageReservation r,Func<bool> cancelled){AdmitOwner(r);cache.PreparePublic(terrain,geometry,profile,r,cancelled);}
        internal void RetireUnusedMaps()=>cache.RetireUnused();
        internal void Prune(NavigationAdmission current){Context previous=null,at=first;while(at!=null){var next=at.Next;bool live=false;foreach(var member in at.Members)if(current.Contains(member)){live=true;break;}
            if(!live){if(previous==null)first=next;else previous.Next=next;at.Next=null;at.Release();}else previous=at;at=next;}}
        // Partial is a terminal incomplete decision, never a Ready route or
        // evidence that the native query exhausted its resource budget.
        internal static NavSolveStatus TerminalStatus(RecastQueryStatus status)
        {
            switch(status){
                case RecastQueryStatus.Full:return NavSolveStatus.Ready;
                case RecastQueryStatus.Partial:return NavSolveStatus.Partial;
                case RecastQueryStatus.Unreachable:return NavSolveStatus.UnreachableInGraph;
                case RecastQueryStatus.ResourceFailure:return NavSolveStatus.CapacityExceeded;
                case RecastQueryStatus.Invalid:return NavSolveStatus.InvalidEndpoint;
                default:throw new ArgumentException("Native query is not terminal.");
            }
        }
        internal long LongQueries{get;private set;}internal long Attachments{get;private set;}internal long Iterations{get;private set;}
        private static bool Compatible(NavigationRequest a,NavigationRequest b){
            var id=a.MemberIdentity;var other=b.MemberIdentity;
            return ReferenceEquals(a,b)||(id!=null&&id.GroupId!=0&&other!=null&&b.Entity>=0&&other.GroupId==id.GroupId&&a.Session==b.Session&&
                a.Profile.Radius==b.Profile.Radius&&a.Profile.LocalGridCell==b.Profile.LocalGridCell&&a.Profile.ProfileId==b.Profile.ProfileId&&a.Profile.Revision==b.Profile.Revision&&a.HeldIdentity==b.HeldIdentity&&ReferenceEquals(a.Geometry,b.Geometry)&&a.StartLocation?.SurfaceId==b.StartLocation?.SurfaceId&&a.GoalLocation?.SurfaceId==b.GoalLocation?.SurfaceId);
        }
        internal bool TryGet(NavigationAdmissionPin observed,NavigationRequest request,ILayeredNavigationProvider provider,INavStorageReservation r,Func<bool> cancelled,out RecastMemberRoute route,out NavSolveStatus status)
        {
            if(disposed)throw new ObjectDisposedException(nameof(RecastGroupPreparation));
            if(cancelled?.Invoke()==true)throw new OperationCanceledException();
            AdmitOwner(r);route=null;status=NavSolveStatus.Pending;
            Context context=null;int index=-1;for(var at=first;at!=null;at=at.Next)if((index=at.Index(request))>=0){context=at;break;}
            if(context==null){
                context=Context.Create(observed,request,r);
                try{
                    if(observed.Value.AuthoredTerrain==null&&(observed.Value.SurfaceProviderId!=NavLocation.FlatSurface||observed.Value.SurfaceSemanticsVersion!=1)||!request.StartLocation.HasValue||!request.GoalLocation.HasValue)context.Result=NavSolveStatus.InvalidEndpoint;
                    else{
                        context.Map=cache.Acquire(observed.Value.AuthoredTerrain,request.Geometry,request.Profile,request.HeldIdentity,r,cancelled);
                        using(var memory=RecastNative.Memory(r))context.Query=context.Map.Map.Query(request.StartLocation.Value,request.GoalLocation.Value);
                        LongQueries++;
                    }
                    context.Next=first;first=context;index=context.Index(request);
                }catch{context.Release();throw;}
            }
            if(context.Query==null){
                if(context.SpineRequired)return false;
                if(context.RawCorridor!=null&&context.Routes==null)Project(context,provider,(RecastRoutePoint[])context.CompletedCorridor?.Value??Array.Empty<RecastRoutePoint>(),r,cancelled,false);
                return Advance(context,index,r,cancelled,out route,out status);
            }
            using(var memory=RecastNative.Memory(r)){
                long before=context.Query.Iterations;var native=context.Query.Step(QueryStepWork);Iterations+=context.Query.Iterations-before;
                if(native==RecastQueryStatus.Pending)return false;
                var result=TerminalStatus(native);
                if(result==NavSolveStatus.Ready)Project(context,provider,context.Query.CopyTypedRoute(),r,cancelled,true);
                context.Result=result;context.Query.Dispose();context.Query=null;
            }
            // Local work begins on the next invocation after the native quantum.
            if(context.SpineRequired)return false;
            return Finish(context,index,out route,out status);
        }
        // Projection keeps the original bounded admission route. Restartable Solve
        // work only supplies a complete paid corridor; fan/route construction and
        // existing result guards run here, outside the per-quantum Solve loop.
        private void Project(Context context,ILayeredNavigationProvider provider,RecastRoutePoint[] corridor,INavStorageReservation r,Func<bool> cancelled,bool allowRepair){
                    // Same existing result limits, with construction/list/digest
                    // copies admitted before allocating the detached proposal.
                    long scratch=checked(8192L+8L*65536*(NavStorageLayout.ValueBytes(typeof(NavTypedLeg))+NavStorageLayout.ValueBytes(typeof(RecastRoutePoint)))+128L*context.Members.Length);
                    using(var workspace=NavStorageScratch.Reserve(r,scratch)){
                        var partition=provider as PartitionNavigationProvider;
                        ILayeredNavigationProvider owned=null;RecastPayload payload=null,rawPayload=null;
                        try{
                            if(context.Provider==null){if(partition!=null)owned=PartitionNavigationProvider.CopyOwned(partition,r);else if(provider is LayeredNavigationProvider layered)owned=LayeredNavigationProvider.CopyOwned(layered,r);}
                            var traceProvider=context.Provider??(ILayeredNavigationProvider)owned??provider;
                            var spine=BuildSpine(traceProvider,context.Members,corridor,context.Map.Map.Geometry);
                            if(spine==null&&allowRepair&&traceProvider is PartitionNavigationProvider){
                                rawPayload=RecastPayload.Allocate(r,checked(32L+corridor.Length*NavStorageLayout.ValueBytes(typeof(RecastRoutePoint))));rawPayload.Value=corridor;context.RequireEdge(rawPayload);
                                if(owned is INavStorageFacade owner)context.RequireEdge(owner.StorageRoot);
                                context.Provider=owned;owned=null;context.RawCorridor=rawPayload;rawPayload=null;context.SpineRequired=true;return;
                            }
                            var routes=new RecastMemberRoute[context.Members.Length];int points=0,legs=0;
                            for(int i=0;i<context.Members.Length;i++){
                                if(cancelled?.Invoke()==true)throw new OperationCanceledException();
                                var path=spine==null?null:Attach(traceProvider,context.Members[i],spine,context.Map.Map.Geometry);
                                if(path!=null){points+=path.Waypoints.Length;legs+=path.Legs.Length;if(points>65536||legs>65536)throw new InvalidOperationException("Recast group result capacity exceeded.");routes[i]=path;}
                            }
                            long bytes=32+8L*routes.Length;
                            if(spine!=null)bytes=checked(bytes+NavStorageLayout.ObjectBytesBound(typeof(RecastSpineProposal))+88+(NavTypedLeg.StorageCharge+NavStorageLayout.ValueBytes(typeof(NavTypedLeg)))*(long)spine.Legs.Length+4L*spine.Stops.Length+24+2L*spine.Binding.Length);
                            foreach(var path in routes)if(path!=null)bytes=checked(bytes+NavStorageLayout.ObjectBytesBound(typeof(RecastMemberRoute))+64+(NavTypedLeg.StorageCharge+NavStorageLayout.ValueBytes(typeof(NavTypedLeg)))*(long)path.Legs.Length+NavStorageLayout.ValueBytes(typeof(RecastRoutePoint))*(long)path.Waypoints.Length);
                            payload=RecastPayload.Allocate(r,bytes);payload.Value=routes;
                            // Publish only after every potentially refusing birth/guard.
                            if(owned!=null){context.Provider=owned;owned=null;}context.ResultPayload=payload;payload=null;context.Routes=routes;Attachments+=context.Members.Length;
                        }finally{(owned as INavStorageFacade)?.StorageRoot.Release();payload?.Dispose();rawPayload?.Dispose();}
                    }
        }
        internal long LocalAttachmentWork{get;private set;}
        private Context Find(NavigationRequest request){for(var c=first;c!=null;c=c.Next)if(c.Index(request)>=0)return c;return null;}
        internal bool HasPendingAttachment(NavigationRequest request)=>Find(request) is Context c&&c.SpineRequired;
        internal int AdvanceAttachment(NavigationRequest request,INavStorageReservation r,int maximumWork,Func<bool> cancelled)
        {
            var c=Find(request);if(c==null||!c.SpineRequired)return 0;
            AdmitOwner(r);NavStorageAdmission.RequireOwned(r,c);
            if(c.SpineRequired){
                if(c.SpinePreparation==null){c.SpinePreparation=RecastSpinePreparation.Create(c.RawCorridor,r);c.RequireEdge(c.SpinePreparation);}
                long before=c.SpinePreparation.Work;
                try{c.SpinePreparation.Step(c.Provider,c.Members[0],c.Map.Map.Geometry,r,Math.Min(QueryStepWork,maximumWork),cancelled);}
                finally{long used=c.SpinePreparation.Work-before;if(used>Math.Min(QueryStepWork,maximumWork))throw new InvalidOperationException("Recast spine quantum exceeded.");LocalAttachmentWork+=used;}
                return checked((int)(c.SpinePreparation.Work-before));
            }
            return 0;
        }
        internal void CommitAttachment(NavigationRequest request)
        {
            var c=Find(request);
            if(c?.SpineRequired==true&&c.SpinePreparation!=null&&c.SpinePreparation.Status!=RecastSpinePreparation.Result.Pending){
                var completed=c.SpinePreparation.Completed;if(completed!=null){c.RequireEdge(completed);completed.Retain();c.CompletedCorridor=completed;}
                c.SpineRequired=false;c.SpinePreparation.Dispose();c.SpinePreparation=null;
            }
        }
        internal void InvalidateAttachment(NavigationRequest request)
        {
            var c=Find(request);if(c?.SpineRequired==true){c.SpinePreparation?.Dispose();c.SpinePreparation=null;return;}

        }
        private bool Advance(Context c,int index,INavStorageReservation r,Func<bool> cancelled,out RecastMemberRoute route,out NavSolveStatus status)
        {route=null;status=NavSolveStatus.Pending;if(c.SpineRequired)return false;return Finish(c,index,out route,out status);}
        private static bool Finish(Context context,int index,out RecastMemberRoute route,out NavSolveStatus status)
        {status=context.Result;route=null;if(status==NavSolveStatus.Ready){route=context.Routes[index];if(route==null)status=NavSolveStatus.BlockedConnector;}return true;}
        private static bool Trace(ILayeredNavigationProvider provider,NavigationRequest request,NavLocation from,NavLocation target,out IReadOnlyList<NavTypedLeg> legs)
        {
            legs=null;double radius=request.Profile.Radius;var clearance=new NavClearanceProfile("ground",1,"ground",0,radius);
            if(!request.Geometry.SegmentFree(from.Position,target.Position,radius))return false;
            if(provider is PartitionNavigationProvider partition){if(!partition.TryTrace(from,target.Position,clearance,out var reached,out var trace)||!reached.Equals(target))return false;legs=trace;return true;}
            return RecastLayerTrace.Try(provider,from,target,clearance,out legs);
        }
        internal static RecastSpineProposal BuildSpine(ILayeredNavigationProvider provider,NavigationRequest[] members,RecastRoutePoint[] corridor,RecastAuthorityGeometry geometry)
        {
            if(!(provider is PartitionNavigationProvider)&&!(provider is LayeredNavigationProvider)||corridor.Length==0)return null;
            var partition=provider;
            var request=members[0];var final=request.GoalLocation.Value;
            // Native raster cells straddle exact authority support boundaries.
            // Reject an invalid original corner without relabelling its area.
            // The next original corner must prove its whole trace from the last
            // accepted point; an essential obstacle turn can never be skipped.
            var points=new List<NavLocation>{request.StartLocation.Value};
            foreach(var point in corridor){
                if(!Trace(partition,request,points[points.Count-1],point.Location,out _)||Math.Abs(geometry.Point(point.Location)[1]-point.Height)>2*geometry.CellHeight+1e-6)continue;
                if(!points[points.Count-1].Equals(point.Location))points.Add(point.Location);
            }
            if(points.Count<2||!points[points.Count-1].Equals(corridor[corridor.Length-1].Location))return null;
            if(!points[points.Count-1].Equals(final)){
                if(points[points.Count-1].SurfaceId!=final.SurfaceId||!Trace(partition,request,points[points.Count-1],final,out _))return null;
                points.Add(final);
            }
            // The fan is a geometric envelope of all already accepted final slots
            // and their footprints. Its size is derived, never a new tuning knob.
            var goals=members.Select(m=>m.GoalLocation.Value).ToArray();
            if(goals.Any(g=>g.SurfaceId!=final.SurfaceId))return null;
            var center=new NavPoint(goals.Average(g=>g.Position.X),goals.Average(g=>g.Position.Z));
            double radius=members.Select((m,i)=>Distance(goals[i].Position,center)+m.Profile.Radius).Max();
            int lastSupport=points.Count-1;while(lastSupport>0&&points[lastSupport-1].SurfaceId==final.SurfaceId)lastSupport--;
            int fan=points.Count-1;
            for(int i=lastSupport;i<points.Count;i++){
                if(Distance(points[i].Position,center)<=radius){fan=i;break;}
                if(i+1>=points.Count)continue;
                var a=points[i].Position;var b=points[i+1].Position;double dx=b.X-a.X,dz=b.Z-a.Z,x=a.X-center.X,z=a.Z-center.Z;
                double aa=dx*dx+dz*dz,bb=2*(x*dx+z*dz),cc=x*x+z*z-radius*radius,disc=bb*bb-4*aa*cc;
                if(aa==0||disc<0)continue;double t=(-bb-Math.Sqrt(disc))/(2*aa);
                if(t<=0||t>=1)continue;
                var entry=new NavLocation(new NavPoint(a.X+t*dx,a.Z+t*dz),final.SurfaceId);
                points.Insert(i+1,entry);fan=i+1;break;
            }
            var legs=new List<NavTypedLeg>();var stops=new List<int>{0};int fanNode=-1;
            if(fan==0)fanNode=0;
            for(int i=1;i<points.Count;i++){
                if(!Trace(partition,request,points[i-1],points[i],out var trace))return null;
                if(legs.Count+trace.Count>16384)throw new InvalidOperationException("Recast spine capacity exceeded.");legs.AddRange(trace);stops.Add(legs.Count);if(i==fan)fanNode=legs.Count;
            }
            if(legs.Count==0||fanNode<0)return null;
            string binding=NavContract.Digest(w=>{w.Write("recast-shared-spine-v2");w.Write(geometry.Binding);w.Write(request.Session);w.Write(request.MemberIdentity?.GroupId??0);w.Write(request.MemberIdentity?.OrderRevision??0);w.Write(request.Profile.Radius);w.Write(request.HeldIdentity??"");
                foreach(var leg in legs){NavContract.Location(w,leg.From);NavContract.Location(w,leg.To);w.Write((int)leg.Kind);w.Write(leg.PortalId??"");w.Write(leg.Cost);}w.Write(fanNode);NavContract.Point(w,center);w.Write(radius);});
            return new RecastSpineProposal(legs.ToArray(),binding,geometry.Binding,fanNode,stops.ToArray(),center,radius);
        }
        private static double Distance(NavPoint a,NavPoint b){double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        internal static RecastMemberRoute Attach(ILayeredNavigationProvider provider,NavigationRequest request,RecastSpineProposal spine,RecastAuthorityGeometry geometry)
        {
            if(!(provider is PartitionNavigationProvider)&&!(provider is LayeredNavigationProvider)||!request.StartLocation.HasValue||!request.GoalLocation.HasValue)return null;
            var partition=provider;
            var start=request.StartLocation.Value;var goal=request.GoalLocation.Value;
            int join=-1;IReadOnlyList<NavTypedLeg> origin=null;
            foreach(int i in spine.Stops.Where(n=>n<=spine.Fan))if(Trace(partition,request,start,spine.Node(i),out origin)){join=i;break;}
            if(join<0)return null;
            int exit=-1;IReadOnlyList<NavTypedLeg> terminal=null;
            foreach(int i in spine.Stops.Where(n=>n>=Math.Max(join,spine.Fan)))if(Trace(partition,request,spine.Node(i),goal,out terminal)){exit=i;break;}
            if(exit<0)return null;
            if(origin.Count+exit-join+terminal.Count>16384)throw new InvalidOperationException("Recast branch capacity exceeded.");
            var legs=new List<NavTypedLeg>();legs.AddRange(origin);int originCount=legs.Count;
            for(int i=join;i<exit;i++)legs.Add(spine.Legs[i]);int terminalIndex=legs.Count;legs.AddRange(terminal);
            // Every Surface endpoint is explicit so paid projection can compare
            // the complete transcript. Portal endpoints keep their typed identity.
            var waypoints=new List<RecastRoutePoint>{new RecastRoutePoint(spine.Node(join),geometry.Point(spine.Node(join))[1])};
            foreach(int n in spine.Stops.Where(n=>n>join&&n<=exit))waypoints.Add(new RecastRoutePoint(spine.Node(n),geometry.Point(spine.Node(n))[1]));
            waypoints.Add(new RecastRoutePoint(goal,geometry.Point(goal)[1]));
            return new RecastMemberRoute(waypoints,legs,spine,join,exit,originCount,terminalIndex);
        }
        public void Dispose(){if(disposed)return;disposed=true;while(first!=null){var next=first.Next;first.Next=null;first.Release();first=next;}cache.Dispose();owner?.Dispose();owner=null;}
    }
}
