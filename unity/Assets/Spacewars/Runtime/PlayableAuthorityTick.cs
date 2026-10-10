using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    public sealed class PlayableRouteBinding
    {
        internal PlayableRouteBinding(NavGeometry geometry,PlayableProfile profile,NavigationAdmission admission=null,NavigationRoutePort port=null){Geometry=geometry;Profile=profile;Admission=admission;RoutePort=port;}
        public NavGeometry Geometry{get;}
        public PlayableProfile Profile{get;}
        public NavigationAdmission Admission{get;}
        public NavigationRoutePort RoutePort{get;}
    }
    // One callable authority pipeline for realtime Player and bounded Unity-host batches.
    // The caller owns this object; only navigation mailboxes cross threads.
    public sealed partial class PlayableAuthorityTick
    {
        public const string RouteDeliveryPolicy = "native-fixed-before-step-v1";
        private readonly PlayableDomain domain;
        private readonly AiAuthorityScheduler scheduler;
        private readonly int seed;
        private readonly Dictionary<string,long> humans = new Dictionary<string,long>();
        private long sequence;
        private bool prepared;
        internal PlayableAuthorityTick(PlayableDomain domain,AiAuthorityScheduler scheduler,int seed,OfflineMatchConfiguration configuration=null)
        {this.domain=domain;this.scheduler=scheduler;this.seed=seed;domain.BindMatchSeed(seed);savedConfiguration=configuration;}
        public PlayableAuthorityTick(OfflineMatchConfiguration configuration,long generation,AiProfile aiProfile=null,AiDifficulty difficulty=AiDifficulty.Fighter)
        {
            if(configuration==null)throw new ArgumentNullException(nameof(configuration));
            domain=new PlayableDomain(configuration.Profile,generation,configuration);seed=configuration.Seed;domain.BindMatchSeed(seed);savedConfiguration=configuration;
            scheduler=new AiAuthorityScheduler(configuration.Roster.Where(p=>p.Control==OfflineControl.Ai).Select(p=>
                new PlayableAiOwnerLoop(configuration.Profile,PlayableAiOpeningComposition.Initialize(seed,p.Id,aiProfile:aiProfile??AiProfile.Initial),generation,null,aiProfile??AiProfile.Initial,difficulty)));
            Latest=domain.PlayerSnapshot(0,RuntimeStatus.Starting,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
        }
        public NavMailbox<NavigationRequest> Requests=>domain.Navigation.Requests;
        public NavMailbox<NavigationAnswer> Answers=>domain.Navigation.Answers;
        public NavGeometry NavigationGeometry=>domain.Geometry;
        public PlayableRouteBinding NavigationBinding=>new PlayableRouteBinding(domain.Geometry,domain.RoutingProfile,domain.Navigation.Admission,domain.Navigation.RoutePort);
        public long Generation=>domain.Navigation.Generation;
        public long Tick=>domain.Tick;
        public bool AwaitingRoutes=>prepared;
        public PlayableSnapshot Latest{get;private set;}
        // Trusted worker ingress still uses ordinary domain admission and receipts.
        public PlayableCommandReceipt Apply(PlayableCommand command)
        {
            if(prepared)throw new InvalidOperationException("Commands must enter before the fixed route barrier.");
            var status=domain.Apply(command,out var message);
            if(status==PlayableCommandStatus.Applied||status==PlayableCommandStatus.Accepted)
                if(command.Origin!=PlayableOrderOrigin.Ai)humans[command.PlayerId]=command.Sequence;
            return new PlayableCommandReceipt(command?.Sequence??0,domain.Tick,status,message,0,command?.PlayerId);
        }
        public bool TryAdvance(bool paused=false)
        {
            if(!TryAdvance(paused,sequence+1,id=>humans.TryGetValue(id,out var value)?value:0,
                ()=>new PlayableRuntimeMetrics(0,0,0,0,0,domain.Navigation.PendingCount),out var snapshot))return false;
            Latest=snapshot;return true;
        }
        internal bool TryAdvance(bool paused,long publication,Func<string,long> human,Func<PlayableRuntimeMetrics> metrics,out PlayableSnapshot snapshot,bool publishWaiting=true)
        {
            snapshot=null;domain.SetRallyPaused(paused);
            if(!paused)
            {
                if(!prepared){scheduler.Deliver(domain,human,false);domain.Navigation.PrepareDeliveryBarrier();prepared=true;}
                // Wall delay never advances gameplay or decision clocks. All active jobs
                // issued before this step deliver together, including explicit failures.
                if(!domain.Navigation.DeliveryBarrierReady)
                {
                    // Applied commands and terminal receipts are observable while routing;
                    // publishing does not step or review the scheduler again.
                    if(publishWaiting)Latest=snapshot=domain.PlayerSnapshot(publication,RuntimeStatus.Running,false,metrics(),null,seed);
                    return false;
                }
                domain.StepWithAiRepairAccounting(1d/30d,scheduler.Owners.Select(o=>o.Budget));prepared=false;
            }
            if(paused)scheduler.Deliver(domain,human,true);
            snapshot=domain.PlayerSnapshot(publication,paused?RuntimeStatus.Paused:RuntimeStatus.Running,paused,metrics(),null,seed);
            scheduler.Review(domain,publication,snapshot.Status,paused,snapshot.Metrics,seed,human);
            sequence=publication;Latest=snapshot;return true;
        }
    }
}
