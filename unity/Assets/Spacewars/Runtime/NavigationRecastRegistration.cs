using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    internal sealed partial class NavigationHostRoot
    {
        internal NavNameKey RegisterRecordedGroundRoute(NavigationRequest request, NavScheduledRequest prepared,
            RecordedGroundRoute recording, string surfaceProviderId, IPlayableTerrain terrain,
            INavStorageReservation reservation)
        {
            if (sealedRoot || faulted) throw new InvalidOperationException("Ground replay requires a writable host root.");
            var expected = recording.Corridor;
            NavigationHostRequest input = null;
            NavigationHostProvider provider = null;
            NavigationHostRow row = null, solvedRow = null, completed = null;
            NavPagedList<NavTypedLeg> legs = null, spineLegs = null;
            NavRecastSpine spine = null;
            NavRecastMemberPlan plan = null;
            NavTypedRoute route = null;
            NavScheduledCompletion solved = null;
            NavigationHostCorridor corridor = null;
            NavigationHostAnswer answer = null;
            try
            {
                input = NavigationHostRequest.CopyOwned(request, prepared.Provider.Binding, reservation);
                provider = NavigationHostProvider.CopyOwned(prepared.Provider, input.Request.Geometry, surfaceProviderId,
                    1, reservation, SharedPartition(prepared.Provider));
                row = NavigationHostRow.CreateRegistered(input, provider, NextProviderIdentity, prepared,
                    reservation, ReplayOnlyProjection);
                var key = NavNameKey.Subscription(row.Scheduled.Subscription);
                if (Requests.ContainsKey(key) || Rows.ContainsKey(key))
                    throw new ArgumentException("Duplicate recorded ground route.");

                legs = RestoreRecordedLegs(expected.Legs, provider.Provider, reservation);
                if (recording.Recast is RecordedRecastPlan recast)
                {
                    var id = row.Scheduled.Subscription;
                    spineLegs = RestoreRecordedLegs(recast.SpineLegs, provider.Provider, reservation);
                    spine = NavRecastSpine.Create(id.WorldGeneration, id.GroupId, id.RootOrderRevision,
                        id.CommandSequence, request.Profile.Radius, recast.Binding, recast.MapBinding,
                        row.Scheduled.GraphKey, provider.Provider.Binding, row.Scheduled.Profile.ExactProfileBinding,
                        request.HeldIdentity, request.Geometry.Revision, recast.FanCenter, recast.FanRadius,
                        recast.FanNode, spineLegs, reservation);
                    plan = NavRecastMemberPlan.Create(spine, recast.Join, recast.Exit, recast.OriginLegs,
                        recast.TerminalLeg, row.Scheduled.GraphKey, row.Scheduled.Profile.ExactProfileBinding, reservation);
                }
                route = NavTypedRoute.CopySubscriptionAndCreateOwned(row.Scheduled.Subscription, legs,
                    legs.Sum(leg => leg.Cost), reservation, plan);
                corridor = NavigationHostCorridor.CreateOwned(input, provider, row.Scheduled, route, reservation);
                if (corridor.Corridor.StableKey != expected.StableKey)
                    throw new InvalidOperationException("Ground replay route diverged from its admitted request.");

                using (NavStorageScratch.Reserve(reservation, checked(256L + 16L * recording.Points.Count)))
                    answer = NavigationHostAnswer.CreateReady(input, corridor, recording.Points.ToArray(), reservation);
                answer.QualifyBeforePublication(reservation, terrain);
                if (answer.QualificationFailure != null && !recording.Failure.HasValue)
                    throw new InvalidOperationException("Recorded route failed qualification: " + answer.QualificationFailure);
                solved = new NavScheduledCompletion(row.Scheduled, NavSolveStatus.Ready, route, 0, 0, reservation.TryReserve);
                solvedRow = row.WithSolved(solved, reservation);
                completed = solvedRow.WithAnswer(answer, reservation);
                AddProvider(provider, reservation);
                Requests.Add(key, input);
                Rows.Add(key, completed);
                Scan.AddLast(key);
                Completion.AddLast(key);
                return key;
            }
            catch
            {
                faulted = true;
                throw;
            }
            finally
            {
                completed?.ReleaseStorage();
                solvedRow?.ReleaseStorage();
                solved?.Release();
                answer?.ReleaseStorage();
                corridor?.ReleaseStorage();
                route?.ReleaseStorage();
                plan?.Release();
                spine?.Release();
                spineLegs?.Release();
                legs?.Release();
                row?.ReleaseStorage();
                provider?.ReleaseStorage();
                input?.ReleaseStorage();
            }
        }

        private static NavPagedList<NavTypedLeg> RestoreRecordedLegs(IReadOnlyList<NavCorridorLeg> source,
            ILayeredNavigationProvider provider, INavStorageReservation reservation)
        {
            var result = NavPagedList<NavTypedLeg>.Create(reservation.TryReserve);
            try
            {
                foreach (var leg in source)
                {
                    var physical = NavTypedLeg.CreateOwned(leg.From, leg.To, leg.Cost, leg.Kind, leg.PortalId,
                        reservation, provider);
                    try { result.Add(physical); }
                    finally { physical.ReleaseStorage(); }
                }
                result.Seal();
                return result;
            }
            catch
            {
                result.Release();
                throw;
            }
        }

        private static bool TraceRecastOwned(ILayeredNavigationProvider provider,NavLocation from,NavLocation target,NavClearanceProfile clearance,INavStorageReservation r,out NavLocation reached,out System.Collections.Generic.IReadOnlyList<NavTypedLeg> trace){
            reached=default;trace=null;if(provider is PartitionNavigationProvider partition)return partition.TryTraceOwned(from,target.Position,clearance,out reached,out trace,r)&&reached.Equals(target);
            if(!RecastLayerTrace.Try(provider,from,target,clearance,out trace,r))return false;reached=target;return true;
        }
        // Stage an already-computed route under the SAME physical provider, request,
        // projection and publication ownership as ordinary host results. No scheduler
        // search or optimality certificate is invoked here.
        internal NavNameKey RegisterRecast(NavigationRequest request,NavScheduledRequest prepared,
            RecastMemberRoute proposal,NavSolveStatus status,string surfaceProviderId,INavStorageReservation reservation)
        {
            if(sealedRoot||request==null||prepared==null||reservation==null||status==NavSolveStatus.Pending)throw new ArgumentException("Invalid Recast registration.");
            NavigationHostRequest input=null;NavigationHostProvider provider=null;NavigationHostRow row=null,next=null;
            NavScheduledCompletion solved=null;NavigationHostAnswer answer=null;NavTypedRoute route=null;NavPagedList<NavTypedLeg> legs=null;NavRecastSpine spine=null;NavRecastMemberPlan plan=null;
            try{
                input=NavigationHostRequest.CopyOwned(request,prepared.Provider.Binding,reservation);
                provider=NavigationHostProvider.CopyOwned(prepared.Provider,input.Request.Geometry,surfaceProviderId,1,reservation,SharedPartition(prepared.Provider));
                row=NavigationHostRow.CreateRegistered(input,provider,NextProviderIdentity,prepared,reservation,ReplayOnlyProjection);
                var key=NavNameKey.Subscription(row.Scheduled.Subscription);
                if(Requests.ContainsKey(key)||Rows.ContainsKey(key))throw new ArgumentException("Duplicate Recast registration.");
                if(status==NavSolveStatus.Ready){
                    var partition=provider.Provider;
                    if(proposal==null||proposal.Waypoints.Length==0)throw new ArgumentException("Missing Recast authority projection.");
                    legs=NavPagedList<NavTypedLeg>.Create(reservation.TryReserve);var at=row.Scheduled.Subscription.Origin;double cost=0;
                    int transcript=0;
                    foreach(var point in proposal.Waypoints){
                        if(!TraceRecastOwned(partition,at,point.Location,row.Scheduled.Clearance,reservation,out var reached,out var trace))throw new ArgumentException("Recast branch lost authority clearance.");
                        try{foreach(var part in trace){
                            if(transcript>=proposal.Legs.Length)throw new ArgumentException("Recast semantic transcript truncated.");var expected=proposal.Legs[transcript++];
                            if(!expected.From.Equals(part.From)||!expected.To.Equals(part.To)||expected.Kind!=part.Kind||expected.PortalId!=part.PortalId||expected.Cost!=part.Cost)throw new ArgumentException("Recast semantic transcript changed.");
                            legs.Add(part);cost+=part.Cost;
                        }}finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
                        at=reached;
                    }
                    if(transcript!=proposal.Legs.Length||!at.Equals(row.Scheduled.Subscription.AssignedEndpoint))throw new ArgumentException("Recast branch endpoint mismatch.");
                    legs.Seal();
                    if(proposal.Spine==null)throw new ArgumentException("Missing accepted Recast spine.");
                    foreach(var entry in Rows.Values.Concat(Delivered.Values)){var existing=(entry as NavigationHostRow)?.Solved?.Route?.RecastPlan?.Spine;if(existing?.Binding==proposal.Spine.Binding){NavStorageAdmission.RequireOwned(reservation,existing);spine=existing;spine.Retain();break;}}
                    if(spine==null){
                        var shared=NavPagedList<NavTypedLeg>.Create(reservation.TryReserve);
                        try{int cursor=0;var origin=proposal.Spine.Node(0);
                            foreach(int stop in proposal.Spine.Stops.Skip(1)){
                                if(!TraceRecastOwned(partition,origin,proposal.Spine.Node(stop),row.Scheduled.Clearance,reservation,out var end,out var trace))throw new ArgumentException("Recast spine lost authority clearance.");
                                try{foreach(var part in trace){var expected=proposal.Spine.Legs[cursor++];if(!expected.From.Equals(part.From)||!expected.To.Equals(part.To)||expected.Kind!=part.Kind||expected.PortalId!=part.PortalId||expected.Cost!=part.Cost)throw new ArgumentException("Recast spine transcript changed.");shared.Add(part);}}finally{(trace as NavOwnedStorage)?.ReleaseOwner();}
                                origin=end;
                            }
                            if(cursor!=proposal.Spine.Legs.Length)throw new ArgumentException("Incomplete Recast spine transcript.");shared.Seal();var id=row.Scheduled.Subscription;
                            spine=NavRecastSpine.Create(id.WorldGeneration,id.GroupId,id.RootOrderRevision,id.CommandSequence,request.Profile.Radius,proposal.Spine.Binding,proposal.Spine.MapBinding,row.Scheduled.GraphKey,provider.Provider.Binding,row.Scheduled.Profile.ExactProfileBinding,request.HeldIdentity,request.Geometry.Revision,proposal.Spine.FanCenter,proposal.Spine.FanRadius,proposal.Spine.Fan,shared,reservation);
                        }finally{shared.Release();}
                    }
                    plan=NavRecastMemberPlan.Create(spine,proposal.Join,proposal.Exit,proposal.OriginLegs,proposal.TerminalLeg,row.Scheduled.GraphKey,row.Scheduled.Profile.ExactProfileBinding,reservation);
                    route=NavTypedRoute.CopySubscriptionAndCreateOwned(row.Scheduled.Subscription,legs,cost,reservation,plan);
                    solved=new NavScheduledCompletion(row.Scheduled,status,route,0,0,reservation.TryReserve);
                    next=row.WithSolved(solved,reservation);Projection.AddLast(key);
                }else{answer=NavigationHostAnswer.CreateTerminal(input,status,reservation);next=row.WithAnswer(answer,reservation);Completion.AddLast(key);}
                AddProvider(provider,reservation);Requests.Add(key,input);Rows.Add(key,next);Scan.AddLast(key);return key;
            }catch{faulted=true;throw;}
            finally{plan?.Release();spine?.Release();legs?.Release();route?.ReleaseStorage();answer?.ReleaseStorage();solved?.Release();next?.ReleaseStorage();row?.ReleaseStorage();provider?.ReleaseStorage();input?.ReleaseStorage();}
        }
    }
    public sealed partial class CertifiedRouteKernel
    {
        private RecastGroupPreparation recastPreparation;
        public long RecastFoundationBuilds=>recastPreparation?.FoundationBuilds??0;
        public long RecastFoundationReuses=>recastPreparation?.FoundationReuses??0;
        public long RecastPrepareCount=>recastPreparation?.PrepareCount??0;
        public long RecastGeometryDigests=>recastPreparation?.GeometryDigests??0;
        public long RecastLongQueries=>recastPreparation?.LongQueries??0;
        public long RecastAttachments=>recastPreparation?.Attachments??0;
        public long RecastIterations=>recastPreparation?.Iterations??0;
        public long RecastColdBuilds=>recastPreparation?.ColdBuilds??0;public long RecastWarmHits=>recastPreparation?.WarmHits??0;
    }
}
