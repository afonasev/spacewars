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
        public const string AsyncRouteDeliveryPolicy = "native-per-unit-before-step-v1";
        private readonly PlayableDomain domain;
        private readonly AiAuthorityScheduler scheduler;
        private readonly int seed;
        private readonly Dictionary<string,long> humans = new Dictionary<string,long>();
        private long sequence;
        private bool prepared;
        private bool asynchronousRoutes;
        private readonly bool performanceTrace=Array.IndexOf(Environment.GetCommandLineArgs(),"-routePerformanceEvidence")>=0;
        public string EffectiveRouteDeliveryPolicy=>asynchronousRoutes?AsyncRouteDeliveryPolicy:RouteDeliveryPolicy;
        internal PlayableAuthorityTick(PlayableDomain domain,AiAuthorityScheduler scheduler,int seed,OfflineMatchConfiguration configuration=null,bool asynchronousRoutes=false)
        {this.domain=domain;this.scheduler=scheduler;this.seed=seed;domain.BindMatchSeed(seed);savedConfiguration=configuration;SetRouteDeliveryPolicy(asynchronousRoutes);}
        private void SetRouteDeliveryPolicy(bool asynchronous)
        {asynchronousRoutes=asynchronous;domain.Navigation.SuspendPendingMovement=asynchronous;}
        public PlayableAuthorityTick(OfflineMatchConfiguration configuration,long generation,AiProfile aiProfile=null,AiDifficulty difficulty=AiDifficulty.Fighter,bool asynchronousRoutes=false)
        {
            if(configuration==null)throw new ArgumentNullException(nameof(configuration));
            domain=new PlayableDomain(configuration.Profile,generation,configuration);seed=configuration.Seed;domain.BindMatchSeed(seed);savedConfiguration=configuration;
            scheduler=new AiAuthorityScheduler(configuration.Roster.Where(p=>p.Control==OfflineControl.Ai).Select(p=>
                new PlayableAiOwnerLoop(configuration.Profile,PlayableAiOpeningComposition.Initialize(seed,p.Id,aiProfile:aiProfile??AiProfile.Initial),generation,null,aiProfile??AiProfile.Initial,difficulty)));
            SetRouteDeliveryPolicy(asynchronousRoutes);
            Latest=domain.PlayerSnapshot(0,RuntimeStatus.Starting,false,new PlayableRuntimeMetrics(0,0,0,0,0),null,seed);
        }
        // Execute first-use simulation/policy code on a detached world before the
        // live clock. No live mailboxes, allocators, receipts or owner state enter it.
        internal long PrepareStartupExecution(Func<bool> stopping=null)
        {
            if(domain.Tick!=0||savedConfiguration==null||stopping?.Invoke()==true)return 0;
            var detachedDomain=new PlayableDomain(savedConfiguration.Profile,Generation,savedConfiguration);
            var detachedScheduler=new AiAuthorityScheduler(scheduler.Owners.Select(o=>o.CreateStartupWarmup()));
            var detached=new PlayableAuthorityTick(detachedDomain,detachedScheduler,seed,savedConfiguration,asynchronousRoutes:true);
            long horizon=Math.Max(1,scheduler.Owners.Select(o=>o.StartupReviewTick).DefaultIfEmpty(1).Max());
            while(detached.Tick<horizon&&stopping?.Invoke()!=true)detached.TryAdvance();
            return detached.Tick;
        }
        internal void PrepareNavigationViews(Func<bool> stopping=null)=>scheduler.PrepareNavigationViews(domain,seed,stopping);
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
            long begin=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0,delivered=begin,stepped=begin;
            snapshot=null;domain.SetRallyPaused(paused);
            if(!paused)
            {
                if(!prepared){scheduler.Deliver(domain,human,false);domain.Navigation.PrepareDeliveryBarrier();prepared=!asynchronousRoutes;}
                delivered=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                // Fixed headless policy freezes clocks until all pre-step jobs deliver.
                // Realtime offline policy installs available answers during Navigation.Step.
                if(!asynchronousRoutes&&!domain.Navigation.DeliveryBarrierReady)
                {
                    // Applied commands and terminal receipts are observable while routing;
                    // publishing does not step or review the scheduler again.
                    if(publishWaiting)Latest=snapshot=domain.PlayerSnapshot(publication,RuntimeStatus.Running,false,metrics(),null,seed);
                    return false;
                }
                domain.StepWithAiRepairAccounting(1d/30d,scheduler.Owners.Select(o=>o.Budget));prepared=false;
                stepped=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
            }
            if(paused)scheduler.Deliver(domain,human,true);
            snapshot=domain.PlayerSnapshot(publication,paused?RuntimeStatus.Paused:RuntimeStatus.Running,paused,metrics(),null,seed);
            long projected=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
            scheduler.Review(domain,publication,snapshot.Status,paused,snapshot.Metrics,seed,human);
            if(performanceTrace){
                long end=System.Diagnostics.Stopwatch.GetTimestamp();double scale=1000d/System.Diagnostics.Stopwatch.Frequency;
                if((end-begin)*scale>=20)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "AUTH_STAGE tick={0} deliver_ms={1:F4} step_ms={2:F4} view_ms={3:F4} ai_ms={4:F4}",
                    domain.Tick,(delivered-begin)*scale,(stepped-delivered)*scale,(projected-stepped)*scale,(end-projected)*scale));
            }
            sequence=publication;Latest=snapshot;return true;
        }
    }
}
