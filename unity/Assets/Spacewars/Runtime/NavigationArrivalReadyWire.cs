using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Spacewars.Simulation;
using static Spacewars.Runtime.WorldWire;
namespace Spacewars.Runtime
{
    public sealed partial class NavigationSession
    {
        private static void WriteReadyArrival(BinaryWriter w,NavigationArrivalWork.ReadyCheckpoint s)
        {
            w.Write(s!=null);if(s==null)return;
            var bytes=Pack(inner=>WriteReadyArrivalPayload(inner,s));ByteArray(w,bytes);
            using(var hash=System.Security.Cryptography.SHA256.Create())ByteArray(w,hash.ComputeHash(bytes));
        }
        private static void WriteFrozenReadyArrival(BinaryWriter w,NavigationArrivalAttemptSnapshot attempt,INavStorageReservation reservation)
        {
            bool ready=attempt.Data.Status==NavArrivalStatus.Ready;w.Write(ready);if(!ready)return;
            // SaveReady creates checkpoint/counter/quota records, proposal and
            // sorted outcome arrays and its continuation digest workspace.
            using(NavStorageScratch.Reserve(reservation,attempt.Work.ReadyCaptureScratchBytes)){
                var saved=attempt.Work.SaveReady();
                using(var bytes=WorldWire.PackOwned(inner=>WriteReadyArrivalPayload(inner,saved),(NavigationRetentionBudget)((INavStorageLedgerIdentity)reservation).Identity)){
                    ByteArray(w,bytes.Bytes);
                    using(NavStorageScratch.Reserve(reservation,4096L))using(var hash=System.Security.Cryptography.SHA256.Create())ByteArray(w,hash.ComputeHash(bytes.Bytes));
                }
            }
        }
        private static void WriteReadyArrivalPayload(BinaryWriter w,NavigationArrivalWork.ReadyCheckpoint s)
        {
            String(w,s.InputKey);String(w,s.Continuation);w.Write(s.Epoch);w.Write(s.Allocation);w.Write(s.Candidate);w.Write(s.Occupancy);w.Write(s.Direct);
            w.Write(s.Remaining);w.Write(s.Member);w.Write(s.CandidateCursor);w.Write(s.OccupancyCursor);
            w.Write(s.Quota.Graph);w.Write(s.Quota.Field);w.Write(s.Quota.Connectors);w.Write(s.Quota.Bookkeeping);w.Write(s.Quota.Maintenance);
            var c=s.Counters;
            foreach(var n in new[]{c.LogicalGraphWork,c.PhysicalGraphWork,c.LogicalFieldWork,c.PhysicalFieldWork,c.ConnectorWork,c.CacheHits,c.GraphBuilds,c.FieldBuilds,c.Cancelled,c.Evictions,c.MaintenanceWork})w.Write(n);
            w.Write(c.PendingSubscriptions);w.Write(c.ReadyResults);w.Write(c.CacheFields);w.Write(c.CacheBytes);w.Write(c.PeakCacheBytes);
            w.Write(c.PeakQueue);w.Write(c.PeakFieldLabels);w.Write(c.PeakFrontier);w.Write(c.RetainedBytes);w.Write(c.PeakRetainedBytes);w.Write(c.PeakQueueAge);
            Array(w,s.Proposals,p=>{
                w.Write(p.Member.RequestSequence);ArrivalLocation(w,p.Endpoint);String(w,p.Proof.StableKey);Number(w,p.Route.Cost);
                w.Write(p.Route.Legs.Count);for(int i=0;i<p.Route.Legs.Count;i++){var l=p.Route.Legs[i];ArrivalLocation(w,l.From);ArrivalLocation(w,l.To);Number(w,l.Cost);w.Write((int)l.Kind);String(w,l.PortalId);}
            });
            Array(w,s.Outcomes,p=>{w.Write(p.Key);w.Write((int)p.Value);});
        }
        private static NavigationArrivalWork.ReadyCheckpoint ReadReadyArrival(BinaryReader r,NavArrivalInput input)
        {
            if(!Boolean(r))return null;
            var bytes=ByteArray(r);var seal=ByteArray(r);
            using(var hash=System.Security.Cryptography.SHA256.Create())if(!hash.ComputeHash(bytes).SequenceEqual(seal))throw new ArgumentException("Ready state digest mismatch.");
            return Unpack(bytes,inner=>ReadReadyArrivalPayload(inner,input));
        }
        private static NavigationArrivalWork.ReadyCheckpoint ReadReadyArrivalPayload(BinaryReader r,NavArrivalInput input)
        {
            var s=new NavigationArrivalWork.ReadyCheckpoint{InputKey=String(r),Continuation=String(r),Epoch=r.ReadInt64(),Allocation=r.ReadInt64(),Candidate=r.ReadInt64(),Occupancy=r.ReadInt64(),Direct=r.ReadInt64(),
                Remaining=r.ReadInt32(),Member=r.ReadInt32(),CandidateCursor=r.ReadInt32(),OccupancyCursor=r.ReadInt32()};
            s.Quota=new NavEpochQuota(r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32());
            s.Counters=new NavSchedulerCounters(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),
                r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt64(),r.ReadInt64(),r.ReadInt32(),r.ReadInt32(),r.ReadInt32(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64());
            var c=s.Counters;
            if(new[]{c.LogicalGraphWork,c.PhysicalGraphWork,c.LogicalFieldWork,c.PhysicalFieldWork,c.ConnectorWork,c.CacheHits,c.GraphBuilds,c.FieldBuilds,c.Cancelled,c.Evictions,c.MaintenanceWork,c.CacheBytes,c.PeakCacheBytes,c.RetainedBytes,c.PeakRetainedBytes,c.PeakQueueAge}.Any(n=>n<0)||c.RetainedBytes>LayeredNavigationScheduler.MaxRetainedBytes||c.CacheFields<0||c.CacheFields>LayeredNavigationScheduler.MaxCachedFields||c.PeakQueue<0||c.PeakQueue>LayeredNavigationScheduler.MaxQueue||c.PeakFieldLabels<0||c.PeakFrontier<0)throw new ArgumentException("Invalid ready logical counters.");
            s.Proposals=Array(r,()=>{
                long request=r.ReadInt64();var endpoint=ArrivalLocation(r);string seal=String(r);double cost=Number(r);
                var legs=Array(r,()=>new NavTypedLeg(ArrivalLocation(r),ArrivalLocation(r),Number(r),EnumValue<NavTypedLegKind>(r),String(r)));
                var member=input.Members.SingleOrDefault(m=>m.RequestSequence==request);if(member==null||cost<0||legs.Any(l=>l.Cost<0)||Math.Abs(legs.Sum(l=>l.Cost)-cost)>1e-8)throw new ArgumentException("Invalid ready typed route owner/cost.");
                var subscription=new NavRouteSubscription(member.WorldGeneration,member.GroupId,member.RootOrderRevision,member.CommandSequence,member.IssuedTick,
                    member.ActivationRevision,member.EntityId,member.Incarnation,member.MemberOrderRevision,member.MobilityRevision,member.RequestSequence,member.Origin,endpoint,member.HoldBinding,NavRouteProvenance.CommandMember);
                var route=new NavTypedRoute(subscription,legs.ToList(),cost);var binding=input.WorkingFor(member);
                var scheduled=NavScheduledRequest.ForSingleton(binding.Provider,member.Clearance,binding.Profile,input.Limits,subscription,input.MaxLabels);
                var proof=new NavArrivalReachabilityProof(input,member,endpoint,scheduled,route);
                if(proof.StableKey!=seal)throw new ArgumentException("Ready typed proof digest mismatch.");
                return new NavArrivalProposal(member,endpoint,route,proof);
            });
            s.Outcomes=Array(r,()=>new KeyValuePair<long,NavArrivalStatus>(r.ReadInt64(),EnumValue<NavArrivalStatus>(r)));return s;
        }
    }
}
