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
        public const string RouteDeliveryPolicy = "native-fixed-tick-quantum-v2";
        internal const string LegacyRouteDeliveryPolicy = "native-fixed-before-step-v1";
        internal const string LegacyAsyncRouteDeliveryPolicy = "native-per-unit-before-step-v1";
        public const string AccruedRouteDeliveryPolicy = "native-per-unit-accrued-credits-v3";
        public const string AsyncRouteDeliveryPolicy = "native-per-unit-tick-quantum-v2";
        private readonly PlayableDomain domain;
        private readonly AiAuthorityScheduler scheduler;
        private readonly int seed;
        private readonly Dictionary<string,long> humans = new Dictionary<string,long>();
        private long sequence;
        internal enum RouteBarrierPhase:byte{Idle,MaintenancePending,WaitingForRoutes}
        private RouteBarrierPhase barrierPhase;
        internal bool MaintenancePendingFor(NavigationSession navigation)=>!asynchronousRoutes&&barrierPhase==RouteBarrierPhase.MaintenancePending&&ReferenceEquals(domain.Navigation,navigation);
        internal void CompleteMaintenanceNoThrow(){barrierPhase=RouteBarrierPhase.WaitingForRoutes;}
        private bool asynchronousRoutes;
        private bool tickQuanta=true;
        private readonly bool performanceTrace=Array.IndexOf(Environment.GetCommandLineArgs(),"-routePerformanceEvidence")>=0;
        internal void EnableAccruedCredits(){if(!asynchronousRoutes||!tickQuanta)throw new InvalidOperationException("Accrued mode requires fresh async authority.");domain.Navigation.EnableAccruedCredits(domain.RoutingProfile.MaxAccruedCredits);}
        public string EffectiveRouteDeliveryPolicy=>domain.Navigation.AccruedCreditsEnabled?AccruedRouteDeliveryPolicy:tickQuanta?(asynchronousRoutes?AsyncRouteDeliveryPolicy:RouteDeliveryPolicy):(asynchronousRoutes?LegacyAsyncRouteDeliveryPolicy:LegacyRouteDeliveryPolicy);
        internal PlayableAuthorityTick(PlayableDomain domain,AiAuthorityScheduler scheduler,int seed,OfflineMatchConfiguration configuration=null,bool asynchronousRoutes=false)
        {this.domain=domain;this.scheduler=scheduler;this.seed=seed;domain.BindMatchSeed(seed);savedConfiguration=configuration;SetRouteDeliveryPolicy(asynchronousRoutes);}
        private void SetRouteDeliveryPolicy(bool asynchronous)
        {asynchronousRoutes=asynchronous;domain.Navigation.SuspendPendingMovement=asynchronous||tickQuanta;domain.Navigation.TickQuantaEnabled=tickQuanta;}
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
            PlayableAuthorityTick detached=null;
            try{
            var detachedScheduler=new AiAuthorityScheduler(scheduler.Owners.Select(o=>o.CreateStartupWarmup()));
            detached=new PlayableAuthorityTick(detachedDomain,detachedScheduler,seed,savedConfiguration,asynchronousRoutes:true);
            long horizon=Math.Max(1,scheduler.Owners.Select(o=>o.StartupReviewTick).DefaultIfEmpty(1).Max());
            while(detached.Tick<horizon&&stopping?.Invoke()!=true)detached.TryAdvance();
            return detached.Tick;
            }finally{try{detached?.Stop();}finally{detachedDomain.Navigation.DestroyNavigationAuthority();}}
        }
        internal void PrepareNavigationViews(Func<bool> stopping=null)=>scheduler.PrepareNavigationViews(domain,seed,stopping);
        public NavMailbox<NavigationRequest> Requests=>domain.Navigation.Requests;
        public NavMailbox<NavigationAnswer> Answers=>domain.Navigation.Answers;
        public NavGeometry NavigationGeometry=>domain.Geometry;
        public PlayableRouteBinding NavigationBinding=>new PlayableRouteBinding(domain.Geometry,domain.RoutingProfile,domain.Navigation.Admission,domain.Navigation.RoutePort);
        public long Generation=>domain.Navigation.Generation;
        public long Tick=>domain.Tick;
        public bool AwaitingRoutes=>barrierPhase!=RouteBarrierPhase.Idle;
        public PlayableSnapshot Latest{get;private set;}
        // Trusted worker ingress still uses ordinary domain admission and receipts.
        public PlayableCommandReceipt Apply(PlayableCommand command)
        {
            if(AwaitingRoutes)throw new InvalidOperationException("Commands must enter before the fixed route barrier.");
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
            using var measured=PerformanceProfile.Measure(ProfileStage.Authority);
            long begin=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0,delivered=begin,stepped=begin;
            long allocatedBefore=performanceTrace?GC.GetAllocatedBytesForCurrentThread():0;
            int gcBefore=performanceTrace?GC.CollectionCount(0):0;
            snapshot=null;domain.SetRallyPaused(paused);
            if(!paused)
            {
                bool maintenanceReady=true;
                if(asynchronousRoutes){scheduler.Deliver(domain,human,false);domain.Navigation.PrepareDeliveryBarrier();}
                else{
                    if(barrierPhase==RouteBarrierPhase.Idle){scheduler.Deliver(domain,human,false);barrierPhase=RouteBarrierPhase.MaintenancePending;}
                    maintenanceReady=barrierPhase!=RouteBarrierPhase.MaintenancePending||domain.Navigation.PrepareDeliveryBarrier(this);
                }
                delivered=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
                // V2 fixed waits only for this tick's bounded quantum; known V1
                // checkpoints retain their all-routes barrier. Async never waits.
                bool quantumReady=!tickQuanta||maintenanceReady&&(domain.Navigation.AccruedCreditsEnabled?domain.Navigation.AccountAccruedCredit(domain.Tick+1,domain.RoutingProfile.NavigationRequestsPerFrame,domain.RoutingProfile.Revision,domain.RoutingProfile.MaxAccruedCredits,domain):domain.Navigation.PrepareTickQuantum(domain.Tick+1,domain.RoutingProfile.NavigationRequestsPerFrame,asynchronousRoutes));
                if(!asynchronousRoutes&&(!maintenanceReady||!(tickQuanta?quantumReady:domain.Navigation.DeliveryBarrierReady)||tickQuanta&&!domain.Navigation.ConsumeFixedTickQuantum(domain.Tick+1)))
                {
                    // Applied commands and terminal receipts are observable while routing;
                    // publishing does not step or review the scheduler again.
                    if(publishWaiting)Latest=snapshot=domain.PlayerSnapshot(publication,RuntimeStatus.Running,false,metrics(),null,seed);
                    return false;
                }
                domain.StepWithAiRepairAccounting(1d/30d,scheduler.Owners.Select(o=>o.Budget));barrierPhase=RouteBarrierPhase.Idle;
                stepped=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
            }
            if(paused)scheduler.Deliver(domain,human,true);
            using(PerformanceProfile.Measure(ProfileStage.PlayerView))snapshot=domain.PlayerSnapshot(publication,paused?RuntimeStatus.Paused:RuntimeStatus.Running,paused,metrics(),null,seed);
            long projected=performanceTrace?System.Diagnostics.Stopwatch.GetTimestamp():0;
            scheduler.Review(domain,publication,snapshot.Status,paused,snapshot.Metrics,seed,human);
            if(performanceTrace){
                long end=System.Diagnostics.Stopwatch.GetTimestamp();double scale=1000d/System.Diagnostics.Stopwatch.Frequency;
                if((end-begin)*scale>=20)Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "AUTH_STAGE tick={0} deliver_ms={1:F4} step_ms={2:F4} view_ms={3:F4} ai_ms={4:F4} allocated_bytes={5} gc0_delta={6} clock={7} frequency={8}",
                    domain.Tick,(delivered-begin)*scale,(stepped-delivered)*scale,(projected-stepped)*scale,(end-projected)*scale,GC.GetAllocatedBytesForCurrentThread()-allocatedBefore,GC.CollectionCount(0)-gcBefore,end,System.Diagnostics.Stopwatch.Frequency));
            }
            sequence=publication;Latest=snapshot;return true;
        }
    }
}
