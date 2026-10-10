using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Runtime;
using Spacewars.Simulation;

namespace Spacewars.Presentation
{
    // One bounded host adapter for ordinary and headless entrypoints. Epochs are
    // solver allotments under the existing frozen gameplay barrier, not ticks.
    internal sealed class CertifiedRouteHost
    {
        private sealed class ProviderEntry
        {
            internal ILayeredNavigationProvider Provider;
            internal int References;
        }
        private sealed class Registered
        {
            internal NavigationRequest Request;
            internal string SurfaceProviderId;
            internal NavScheduledRequest Scheduled;
            internal NavigationAnswer Completion;
            internal NavScheduledCompletion Solved;
            internal int LegCursor, ReplayCursor;
            internal NavLocation Expected, At;
            internal List<NavPoint> Points;
            internal List<string> TypedPortals, ObservedPortals;
        }
        private readonly LayeredNavigationScheduler scheduler = new LayeredNavigationScheduler();
        private readonly Dictionary<Tuple<NavGeometry,string>,ProviderEntry> providers =
            new Dictionary<Tuple<NavGeometry,string>,ProviderEntry>();
        private long providerBytes;
        private long heldProjectionBytes,hostAnswerBytes,peakRetainedBytes;
        private readonly Dictionary<string, Registered> registered = new Dictionary<string, Registered>(StringComparer.Ordinal);
        private readonly Queue<string> scanQueue = new Queue<string>();
        private readonly Queue<string> projectionQueue = new Queue<string>();
        private readonly Queue<string> completionQueue = new Queue<string>();
        private long epoch;
        private NavigationRoutePort currentPort;
        private const int WorkAllotment = 16384;
        private const int WorkUnitsPerRequestBudget = 1024;
        private static readonly NavEpochQuota Quota = new NavEpochQuota(WorkAllotment, WorkAllotment,
            WorkAllotment, WorkAllotment * 3, 256);
        private static readonly NavGraphCompileLimits Limits = new NavGraphCompileLimits(1048576, 300000, 1200000);
        internal NavSchedulerCounters Counters => scheduler.Counters;
        internal long SolverEpoch => epoch;
        internal string LastProjectionFailure { get; private set; }
        internal long RetainedBytes => scheduler.Counters.RetainedBytes+ExternalBytes();
        internal long PeakRetainedBytes => peakRetainedBytes;
        private long ExternalBytes()=>providerBytes-scheduler.ProviderRetainedBytes+
            heldProjectionBytes+hostAnswerBytes+128L*registered.Count+
            (currentPort?.CorridorRetainedBytes??0);
        private void UpdateReservation(long nextRegistration=0)
        {scheduler.ReserveExternalBytes(ExternalBytes()+nextRegistration);
            peakRetainedBytes=Math.Max(peakRetainedBytes,RetainedBytes);
            currentPort?.SetHostRetainedBytes(RetainedBytes-currentPort.CorridorRetainedBytes);}
        internal void Clear()
        {
            var oldPort=currentPort;
            if(oldPort==null){ClearOwned();return;}
            oldPort.RunHostStep(()=>{ClearOwned();oldPort.SetHostRetainedBytes(0);});
        }
        // Scheduler ClearWorld retains epoch and quota history across authority ports.
        private void ClearOwned()
        {scheduler.ClearWorld();registered.Clear();providers.Clear();providerBytes=heldProjectionBytes=hostAnswerBytes=peakRetainedBytes=0;scanQueue.Clear();projectionQueue.Clear();completionQueue.Clear();currentPort=null;}

        internal void Service(PlayableRouteBinding binding, int budget)
        {
            var port = binding.RoutePort;
            if(port == null)throw new ArgumentException("Missing route authority facade.");
            if(!ReferenceEquals(currentPort, port)) {
                Clear();currentPort=port;
            }
            port.RunHostStep(()=>ServiceLocked(port,budget));
        }

        private void ServiceLocked(NavigationRoutePort port,int budget)
        {
            UpdateReservation();
            Flush(port,budget);
            var admission=port.Admission;
            if(admission==null)return;
            // Old accepted inputs are kept until their answer can enter the
            // mailbox. Discharge stale ownership before asking the solver to work.
            int scans=Math.Min(budget,scanQueue.Count);
            for(int i=0;i<scans;i++) {
                var key=scanQueue.Dequeue();
                if(!registered.TryGetValue(key,out var row))continue;
                if(port.Admission.Contains(row.Request)){scanQueue.Enqueue(key);continue;}
                scheduler.Cancel(key);
                SetCompletion(key,row,new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),NavSolveStatus.Stale));
            }
            Flush(port,budget);
            for(int i=0;i<budget && registered.Count<LayeredNavigationScheduler.MaxQueue;i++) {
                if(RetainedBytes+128>LayeredNavigationScheduler.MaxRetainedBytes)break;
                UpdateReservation(128);
                if(port.TryDropStale(out _))continue;
                NavScheduledRequest prepared=null;
                NavigationRequest preparedInput=null;
                bool oversized=false;
                bool accepted;
                try{accepted=port.TryTransfer(scheduler,(r,observed)=>{
                        preparedInput=r;return prepared=Prepare(observed,r,out oversized);},out var transferredRequest);
                    if(!accepted){if(preparedInput!=null)DropUnusedProvider(preparedInput,prepared?.Provider);
                        if(oversized&&port.TryRejectUnserviceable(preparedInput,out _)){
                            UpdateReservation();continue;
                        }
                        UpdateReservation();break;}
                    var key=prepared.Subscription.StableKey;
                    registered.Add(key,new Registered{Request=transferredRequest,Scheduled=prepared,
                        SurfaceProviderId=admission.SurfaceProviderId});
                    RetainProvider(transferredRequest,prepared.Provider);scanQueue.Enqueue(key);UpdateReservation();
                }catch{if(preparedInput!=null)DropUnusedProvider(preparedInput,prepared?.Provider);
                    UpdateReservation();throw;}
            }
            UpdateReservation();
            int workLimit=checked(budget*WorkUnitsPerRequestBudget);
            bool Current(NavScheduledIdentity id)
            {
                var latest=port.Admission;
                return latest!=null&&registered.TryGetValue(id.StableKey,out var row)&&latest.Contains(row.Request);
            }
            int consumed=scheduler.Advance(epoch,Quota,Current,workLimit);
            if(consumed==0&&scheduler.NeedsNextEpoch) {
                epoch++;
                scheduler.Advance(epoch,Quota,Current,workLimit);
            }
            for(int i=0;i<budget&&scheduler.ReadyCount>0;i++) {
                if(RetainedBytes+scheduler.ReadyHeadProjectionReserve>LayeredNavigationScheduler.MaxRetainedBytes)break;
                if(!scheduler.TryTake(_=>true,out var completed))continue;
                if(!registered.TryGetValue(completed.Identity.StableKey,out var row))continue;
                if(!port.Admission.Contains(row.Request))
                    SetCompletion(completed.Identity.StableKey,row,new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),NavSolveStatus.Stale));
                else if(completed.Status!=NavSolveStatus.Ready)
                    SetCompletion(completed.Identity.StableKey,row,new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),completed.Status));
                else {row.Solved=completed;heldProjectionBytes+=1024L+288L*completed.Route.Legs.Count;
                    projectionQueue.Enqueue(completed.Identity.StableKey);UpdateReservation();}
            }
            Project(budget*16);
            Flush(port,budget);
        }

        private void SetCompletion(string key,Registered row,NavigationAnswer answer)
        {long released=row.Solved==null?0:1024L+288L*row.Solved.Route.Legs.Count;
            long prior=row.Completion==null?0:AnswerBytes(row.Completion);
            if(answer.Corridor!=null&&RetainedBytes-released-prior+AnswerBytes(answer)>
                LayeredNavigationScheduler.MaxRetainedBytes)
                answer=new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),NavSolveStatus.CapacityExceeded);
            if(row.Completion==null)completionQueue.Enqueue(key);
            else hostAnswerBytes-=prior;
            row.Completion=answer;hostAnswerBytes+=AnswerBytes(answer);
            if(row.Solved!=null)heldProjectionBytes-=1024L+288L*row.Solved.Route.Legs.Count;
            if(row.Scheduled!=null)ReleaseProvider(row.Request,row.Scheduled.Provider);
            row.Scheduled=null;row.Solved=null;row.Points=null;row.TypedPortals=null;row.ObservedPortals=null;UpdateReservation();}

        private static long AnswerBytes(NavigationAnswer answer)
            =>checked(1024L+16L*answer.RouteLength+(answer.Corridor?.EstimatedRetainedBytes??0));

        private void Flush(NavigationRoutePort port,int budget)
        {
            for(int i=0;i<budget&&completionQueue.Count>0;i++) {
                var key=completionQueue.Peek();
                if(!registered.TryGetValue(key,out var row)){completionQueue.Dequeue();continue;}
                if(!port.TryComplete(row.Completion))break; // answer mailbox backpressure
                completionQueue.Dequeue();registered.Remove(key);
                hostAnswerBytes-=AnswerBytes(row.Completion);UpdateReservation();
            }
        }

        private static Tuple<NavGeometry,string> ProviderKey(NavigationRequest request)
            =>Tuple.Create(request.Geometry,request.HeldIdentity);
        private void RetainProvider(NavigationRequest request,ILayeredNavigationProvider provider)
        {var entry=providers[ProviderKey(request)];if(!ReferenceEquals(entry.Provider,provider))throw new InvalidOperationException("Provider reference changed at admission.");entry.References++;}
        private void ReleaseProvider(NavigationRequest request,ILayeredNavigationProvider provider)
        {var key=ProviderKey(request);var entry=providers[key];if(!ReferenceEquals(entry.Provider,provider)||entry.References<1)throw new InvalidOperationException("Unbalanced provider ownership.");
            if(--entry.References==0){providers.Remove(key);providerBytes-=entry.Provider.EstimatedRetainedBytes;}}
        private void DropUnusedProvider(NavigationRequest request,ILayeredNavigationProvider provider)
        {var key=ProviderKey(request);if(providers.TryGetValue(key,out var entry)&&entry.References==0&&
            (provider==null||ReferenceEquals(entry.Provider,provider))){providers.Remove(key);providerBytes-=entry.Provider.EstimatedRetainedBytes;}}

        private NavScheduledRequest Prepare(NavigationAdmission admission,NavigationRequest request,out bool oversized)
        {
            oversized=false;
            if(!admission.Contains(request))throw new InvalidOperationException("Unpublished route input.");
            var provider=Provider(admission,request,out oversized);
            if(provider==null)return null; // bounded backpressure, original FIFO head stays queued
            var identity=request.MemberIdentity;
            NavigationAdmissionGroup group=null;
            if(identity!=null&&identity.GroupId!=0)
                group=admission.Groups.FirstOrDefault(g=>g.GroupId==identity.GroupId);
            NavLocation origin=request.StartLocation??Locate(provider,request.Start,request.Profile.Radius);
            NavLocation endpoint=request.GoalLocation??Locate(provider,request.Goal,request.Profile.Radius);
            var subscription=new NavRouteSubscription(request.Session,identity?.GroupId??0,
                group?.RootOrderRevision??0,group?.CommandSequence??0,group?.IssuedTick??0,
                identity?.OrderRevision??0,request.Entity,identity?.Incarnation??0,
                identity?.OrderRevision??request.Order,identity?.MobilityRevision??0,request.Request,
                origin,endpoint,request.HeldIdentity,
                request.Entity<0?NavRouteProvenance.ProducerProbe:group==null?
                    NavRouteProvenance.LegacySingleton:NavRouteProvenance.CommandMember);
            var clearance=new NavClearanceProfile("ground",1,"ground",(int)(identity?.MobilityRevision??0),request.Profile.Radius);
            var profile=new NavGraphProfile(NavigationAdmission.ExactProfileBinding(request.Profile),"distance",1,request.Profile.GridCell);
            NavScheduledRequest scheduled;
            if(group!=null&&group.Members.Count>1) {
                var terminal=group.OriginalTerminal??Locate(provider,group.OriginalGoal,request.Profile.Radius);
                if(provider.IsValid(terminal,clearance)) {
                    var region=new NavTerminalRegion("accepted-terminal",1,terminal,new[]{terminal});
                    scheduled=new NavScheduledRequest(provider,clearance,profile,Limits,region,subscription,1048576);
                }else scheduled=NavScheduledRequest.ForSingleton(provider,clearance,profile,Limits,subscription,1048576);
            }else scheduled=NavScheduledRequest.ForSingleton(provider,clearance,profile,Limits,subscription,1048576);
            // Submit will take over the scheduler's first reference. Exclude the
            // same provider from the external reservation for that atomic handoff.
            long transferring=scheduler.HasProviderReference(provider)?0:provider.EstimatedRetainedBytes;
            scheduler.ReserveExternalBytes(ExternalBytes()+128-transferring);
            long admissionBytes=4096L+(scheduled.Region==null?0:128L*scheduled.Region.Candidates.Count)+
                transferring+(scheduler.PendingCount==0?2048:0);
            if(!scheduler.TryMakeRoomForExternal(admissionBytes,1))return null;
            return scheduled;
        }

        private static NavLocation Locate(ILayeredNavigationProvider provider,NavPoint point,double radius)
        {
            var clearance=new NavClearanceProfile("ground",1,"ground",0,radius);
            if(provider is PartitionNavigationProvider partition&&partition.TryLocate(point,clearance,out var found))return found;
            if(provider is LayeredNavigationProvider&&provider.IsValid(new NavLocation(point,NavLocation.FlatSurface),clearance))
                return new NavLocation(point,NavLocation.FlatSurface);
            // Preserve the exact point in a typed invalid-endpoint proposal.
            return new NavLocation(point,NavLocation.FlatSurface);
        }

        private ILayeredNavigationProvider Provider(NavigationAdmission admission,NavigationRequest request,out bool oversized)
        {
            oversized=false;
            var terrain=admission.AuthoredTerrain;
            var key=ProviderKey(request);
            if(providers.TryGetValue(key,out var existing))return existing.Provider;
            ILayeredNavigationProvider provider;
            if(terrain!=null)provider=new PartitionNavigationProvider(terrain,request.Geometry,request.HeldIdentity);
            else if(admission.SurfaceProviderId==NavLocation.FlatSurface)provider=NavigationAdmission.CertifiedFlatProvider(request.Geometry);
            else throw new InvalidOperationException("Unsupported authored route provider.");
            if(provider.EstimatedRetainedBytes>LayeredNavigationScheduler.MaxRetainedBytes-4224){oversized=true;return null;}
            if(!scheduler.TryMakeRoomForExternal(provider.EstimatedRetainedBytes+4224,1))return null;
            providers.Add(key,new ProviderEntry{Provider=provider});providerBytes+=provider.EstimatedRetainedBytes;
            UpdateReservation(128);
            return provider;
        }

        private void Project(int work)
        {
            for(int used=0;used<work&&projectionQueue.Count>0;used++) {
                var key=projectionQueue.Peek();
                if(!registered.TryGetValue(key,out var row)||row.Completion!=null||row.Solved==null){projectionQueue.Dequeue();continue;}
                var request=row.Scheduled;var route=row.Solved.Route;
                if(route==null||route.Subscription.StableKey!=key||route.Legs.Count==0){Fail("route identity/legs");continue;}
                if(row.Points==null){row.Points=new List<NavPoint>();row.TypedPortals=new List<string>();
                    row.ObservedPortals=new List<string>();row.Expected=request.Subscription.Origin;row.At=row.Expected;}
                if(row.LegCursor<route.Legs.Count){
                    var leg=route.Legs[row.LegCursor++];
                    if(!leg.From.Equals(row.Expected)){Fail("leg continuity");continue;}
                    if(leg.Kind==NavTypedLegKind.Surface){
                        bool qualified=request.Provider is PartitionNavigationProvider
                            ? request.Provider.IsValid(leg.From,request.Clearance)&&
                                request.Provider.IsValid(leg.To,request.Clearance)&&
                                row.Request.Geometry.SegmentFree(leg.From.Position,leg.To.Position,request.Clearance.Radius)
                            : request.Provider.CanSweep(leg.From,leg.To,request.Clearance);
                        if(!qualified){Fail("surface qualification");continue;}
                        row.Points.Add(leg.To.Position);
                    }else if(request.Provider is PartitionNavigationProvider partition){
                        if(!leg.From.Position.Equals(leg.To.Position)||
                            !partition.Seams.Any(s=>s.Allowed&&s.Id==leg.PortalId&&
                                ((s.FromSurface==leg.From.SurfaceId&&s.ToSurface==leg.To.SurfaceId)||
                                 (s.ToSurface==leg.From.SurfaceId&&s.FromSurface==leg.To.SurfaceId))&&
                                OnSegment(leg.From.Position,s.From,s.To))){Fail("portal qualification");continue;}
                        row.TypedPortals.Add(leg.PortalId);
                    }else{Fail("overlapping surface");continue;} // overlapping-floor XZ projection is unsupported
                    row.Expected=leg.To;continue;
                }
                if(row.ReplayCursor<row.Points.Count&&request.Provider is PartitionNavigationProvider actual){
                    if(!actual.TryTrace(row.At,row.Points[row.ReplayCursor++],request.Clearance,out var reached,out var trace)){Fail("XZ replay trace");continue;}
                    foreach(var leg in trace)if(leg.Kind==NavTypedLegKind.Portal)row.ObservedPortals.Add(leg.PortalId);
                    row.At=reached;continue;
                }
                bool valid=row.Expected.Equals(request.Subscription.AssignedEndpoint)&&row.Points.Count>0&&
                    row.Points.Last().Equals(row.Expected.Position)&&
                    (!(request.Provider is PartitionNavigationProvider)||
                        row.At.Equals(row.Expected)&&row.TypedPortals.SequenceEqual(row.ObservedPortals));
                if(!valid){Fail("XZ replay sequence/end");continue;}
                var identity=request.Subscription;
                var corridor=new NavCorridorDescriptor(NavCorridorDescriptor.CurrentVersion,request.GraphKey,
                    request.Provider.Binding,request.Profile.ExactProfileBinding,
                    row.SurfaceProviderId,row.Request.HeldIdentity,row.Request.BaseGeometry.Revision,request.Clearance.Radius,
                    request.Profile.GridCell,identity.Provenance,
                    identity.WorldGeneration,identity.GroupId,identity.RootOrderRevision,
                    identity.CommandSequence,identity.IssuedTick,
                    identity.ActivationRevision,identity.EntityId,identity.Incarnation,
                    identity.MobilityRevision,identity.RequestSequence,row.Request.Order,
                    identity.Origin,identity.AssignedEndpoint,
                    route.Legs.Select(leg=>new NavCorridorLeg(leg.From,leg.To,leg.Kind,leg.PortalId,
                        leg.Cost,NavCorridorSection.IndividualConnector,0,0)));
                SetCompletion(key,row,new NavigationAnswer(row.Request,row.Points.ToArray(),NavSolveStatus.Ready,corridor));
                projectionQueue.Dequeue();

                void Fail(string reason){LastProjectionFailure=reason;SetCompletion(key,row,new NavigationAnswer(row.Request,Array.Empty<NavPoint>(),NavSolveStatus.BlockedConnector));projectionQueue.Dequeue();}
            }
        }
        private static bool OnSegment(NavPoint point,NavPoint a,NavPoint b)
        {
            double x=b.X-a.X,z=b.Z-a.Z,length=x*x+z*z;
            double t=length==0?0:Math.Max(0,Math.Min(1,((point.X-a.X)*x+(point.Z-a.Z)*z)/length));
            double dx=point.X-a.X-t*x,dz=point.Z-a.Z-t*z;
            return dx*dx+dz*dz<=1e-14;
        }
    }
}
