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
                owner.ReconcileBudget(domain);
                if(!domain.Authorizes(owner.OwnerId)){owner.Stop(domain.Tick);continue;}
                var snapshot=domain.PlayerSnapshotForArmyAi(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),!paused&&owner.NeedsArmyRoutes(domain.Tick),owner.ArmyState);
                if(!paused&&!owner.NeedsArmyRoutes(domain.Tick)&&owner.NeedsDefenseRoutes(snapshot))snapshot=domain.PlayerSnapshotForArmyAi(sequence,status,paused,metrics,null,seed,domain.OwnerFor(owner.OwnerId),true,owner.ArmyState);
                owner.Review(snapshot,human(owner.OwnerId));
                domain.RecordAiState(owner.Checkpoint);
            }
        }
        internal void Rebind(PlayableProfile profile){foreach(var owner in owners)owner.Rebind(profile);}
        internal void Stop(long tick){foreach(var owner in owners)owner.Stop(tick);}
    }
}
