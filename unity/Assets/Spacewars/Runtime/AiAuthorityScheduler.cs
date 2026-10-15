using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    // The ordinary Player, roster runtime and later headless host share this authority path.
    internal sealed partial class AiAuthorityScheduler
    {
        private readonly PlayableAiOwnerLoop[] owners;
        private readonly bool performanceTrace=Array.IndexOf(Environment.GetCommandLineArgs(),"-routePerformanceEvidence")>=0;
        private long nextActionId;
        internal AiAuthorityScheduler(IEnumerable<PlayableAiOwnerLoop> loops)
        {
            owners=loops.Where(x=>x!=null).OrderBy(x=>x.OwnerId,StringComparer.Ordinal).ToArray();
            if(owners.Select(x=>x.OwnerId).Distinct(StringComparer.Ordinal).Count()!=owners.Length)throw new ArgumentException("Duplicate AI owner.");
            foreach(var owner in owners)owner.BindActionAllocator(()=>checked(++nextActionId));
        }
        internal IReadOnlyList<PlayableAiOwnerLoop> Owners=>Array.AsReadOnly(owners);
        internal void Deliver(PlayableDomain domain,Func<string,long> human,bool paused)
        {foreach(var owner in owners){if(!paused)owner.GuardDefenseDelivery(domain);owner.Deliver(domain,human(owner.OwnerId),paused);}}
        internal void Review(PlayableDomain domain,long sequence,RuntimeStatus status,bool paused,PlayableRuntimeMetrics metrics,int seed,Func<string,long> human)
        {
            foreach(var owner in owners)
            {
                long begin=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                owner.ReconcileBudget(domain);
                if(!domain.Authorizes(owner.OwnerId)){owner.Stop(domain.Tick);continue;}
                long budget=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                var snapshot=domain.PlayerSnapshotForArmyAi(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),!paused&&owner.NeedsArmyRoutes(domain.Tick),owner.ArmyState);
                if(!paused&&!owner.NeedsArmyRoutes(domain.Tick)&&owner.NeedsDefenseRoutes(snapshot))snapshot=domain.PlayerSnapshotForArmyAi(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),true,owner.ArmyState);
                long projection=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                owner.Review(snapshot,human(owner.OwnerId));
                long review=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                domain.RecordAiState(owner.Checkpoint);
                if(performanceTrace){
                    long end=System.Diagnostics.Stopwatch.GetTimestamp();double scale=1000d/System.Diagnostics.Stopwatch.Frequency;
                    if((end-begin)*scale>=20)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "AI_STAGE tick={0} owner={1} budget_ms={2:F4} projection_ms={3:F4} review_ms={4:F4} record_ms={5:F4}",
                        domain.Tick,owner.OwnerId,(budget-begin)*scale,(projection-budget)*scale,(review-projection)*scale,(end-review)*scale));
                }
            }
        }
        internal void PrepareNavigationViews(PlayableDomain domain,int seed,Func<bool> stopping=null)
        {
            foreach(var owner in owners){
                if(stopping?.Invoke()==true)break;
                if(domain.Authorizes(owner.OwnerId))domain.PreparePublicNavigation(domain.OwnerFor(owner.OwnerId),stopping);
            }
        }
        internal void Rebind(PlayableProfile profile){foreach(var owner in owners)owner.Rebind(profile);}
        internal void Stop(long tick){foreach(var owner in owners)owner.Stop(tick);}
    }
}
