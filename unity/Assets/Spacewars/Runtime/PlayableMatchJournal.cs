using System;
using System.Collections.Generic;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;

namespace Spacewars.Runtime
{
    // Trusted local diagnostics only. Capture runs on authority; a sink must not retain
    // authority or query it from another thread. No journal data enters observations/saves.
    public interface IPlayableMatchJournal
    {
        void Capture(PlayableAuthorityTick authority,bool paused);
        void Command(PlayableCommand command);
        void Receipt(PlayableCommandReceipt receipt);
        void Failure(Exception error,long tick);
        void Complete(PlayableSnapshot final,MatchResult result);
    }
    public interface IAcceptedRouteJournal
    {
        void AcceptedRoute(string ownerId, long tick, int ordinal, int entity, long request, long order,
            IReadOnlyList<NavPoint> points, NavCorridorDescriptor corridor);
    }
    public interface IReissuedRouteJournal
    {
        void ReissuedRoute(string ownerId, long tick, int ordinal, int entity, long request, long order,
            IReadOnlyList<NavPoint> points, NavCorridorDescriptor corridor);
    }
    public interface IFailedGroundRouteJournal
    {
        void FailedGroundRoute(string ownerId, long tick, int ordinal, NavigationAnswer answer, NavigationOutcome outcome);
    }
    public interface IAssignedGroundSlotJournal
    {
        void GroundSlot(string owner, long tick, int ordinal, long generation, int entity, long request, long order,
            NavLocation endpoint);
    }
    public interface IAcceptedFlightJournal
    {
        void AcceptedFlight(string ownerId, long tick, int ordinal, int entity, PlayableOrderStamp stamp,
            NavPoint position, NavPoint destination, int targetId, bool moving, bool held);
    }
    public readonly struct AiReviewTiming
    {
        public readonly double? CaptureMilliseconds, QueueMilliseconds, ComputeMilliseconds;
        public readonly double? RouteMilliseconds, ReadyToPollMilliseconds, AdoptionMilliseconds;

        internal AiReviewTiming(long capture, long queued, long started, long routeStart, long routeEnd,
            long finished, long polled, long settled)
        {
            CaptureMilliseconds = Milliseconds(capture, queued);
            QueueMilliseconds = Milliseconds(queued, started);
            ComputeMilliseconds = Milliseconds(started, finished);
            RouteMilliseconds = Milliseconds(routeStart, routeEnd);
            ReadyToPollMilliseconds = Milliseconds(finished, polled);
            AdoptionMilliseconds = Milliseconds(polled, settled);
        }

        private static double? Milliseconds(long start, long end) =>
            start > 0 && end >= start ? (end - start) * 1000d / System.Diagnostics.Stopwatch.Frequency : (double?)null;
    }

    public interface IAiDecisionJournal
    {
        void DecisionTiming(string owner,long observationTick,long tick,string status,AiReviewTiming phases);
    }
    public sealed class PlayableRepairCancellation
    {
        public long Tick{get;}
        public string OwnerId{get;}
        public int UnitId{get;}
        public int TowerId{get;}
        public long ActionId{get;}
        public long CommandSequence{get;}
        public NavPoint Position{get;}
        public string SurfaceId{get;}
        public double? Displacement{get;}
        public string Reason{get;}
        public bool RetryBlocked{get;}

        internal PlayableRepairCancellation(long tick,string ownerId,int unitId,int towerId,PlayableOrderStamp stamp,
            NavLocation location,double? displacement,string reason,bool retryBlocked)
        {
            Tick=tick;OwnerId=ownerId;UnitId=unitId;TowerId=towerId;ActionId=stamp.ActionId;CommandSequence=stamp.Sequence;
            Position=location.Position;SurfaceId=location.SurfaceId;Displacement=displacement;Reason=reason;RetryBlocked=retryBlocked;
        }
    }

    internal sealed partial class PlayableDomain
    {
        List<PlayableRepairCancellation> repairCancellations;
        internal void EnableRepairCancellationJournal()=>repairCancellations=new List<PlayableRepairCancellation>();
        internal Action<string, int, PlayableOrderStamp, NavPoint, NavPoint, int, bool, bool> FlightDecisionAccepted;
        private void NotifyFlightDecision(Unit unit)
        {
            var actor = unit.Air.Actor;
            FlightDecisionAccepted?.Invoke(FinancialOwnerId(unit.Owner), unit.Id, unit.LastOrder,
                actor.Position, actor.Goal, unit.CurrentOrder?.TargetId ?? unit.Target, actor.Moving, actor.Held);
        }
        internal string RouteJournalOwner(int entity) =>
            units.TryGetValue(entity, out var unit) ? FinancialOwnerId(unit.Owner) : null;
        internal void RecordRepairCancellation(string ownerId,int unitId,int towerId,PlayableOrderStamp stamp,
            NavLocation location,double? displacement,string reason,bool retryBlocked)
        {
            repairCancellations?.Add(new PlayableRepairCancellation(Tick,ownerId,unitId,towerId,stamp,location,displacement,reason,retryBlocked));
        }
        internal PlayableRepairCancellation[] DrainRepairCancellations()
        {
            if(repairCancellations==null||repairCancellations.Count==0)return Array.Empty<PlayableRepairCancellation>();
            var result=repairCancellations.ToArray();
            repairCancellations.Clear();
            return result;
        }
    }

    public sealed class PlayableMatchJournalContext
    {
        public readonly PlayableProfile Profile;
        public readonly OfflineMatchConfiguration Configuration;
        public readonly AiProfile Ai;
        public readonly long Generation;
        public readonly int Seed;
        public readonly bool Spectator,Restored;
        public readonly long StartTick;
        public readonly string[] Owners,AiOwners;
        public PlayableMatchJournalContext(PlayableProfile profile,OfflineMatchConfiguration configuration,AiProfile ai,long generation,int seed,bool spectator,string[] owners,string[] aiOwners,long startTick=0,bool restored=false)
        {Profile=profile;Configuration=configuration;Ai=ai;Generation=generation;Seed=seed;Spectator=spectator;Owners=owners;AiOwners=aiOwners;StartTick=startTick;Restored=restored;}
    }
    public sealed partial class PlayableRuntime
    {
        // Presentation installs the factory only in test builds. Null is zero-I/O production.
        public static Func<PlayableMatchJournalContext,IPlayableMatchJournal> JournalFactory;
        public static Action<Exception> JournalFailureReported;
        IPlayableMatchJournal matchJournal;
        int journalFailed;
        public string JournalFailure{get;private set;}
        void InitializeJournal(PlayableProfile profile,bool restored=false)
        {
            if(JournalFactory==null)return;
            try{matchJournal=JournalFactory(new PlayableMatchJournalContext(profile,offlineConfiguration,NativeAiProfile,Generation,seed,localSpectator,authorityTick.CaptureDiagnosticOwnerIds(),System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(aiScheduler.Owners,o=>o.OwnerId)),authorityTick.Tick,restored));
                if(matchJournal!=null)domain.EnableRepairCancellationJournal();
                long previousTick = -1;
                int ordinal = 0;
                int DecisionOrdinal(long tick)
                {
                    if (tick != previousTick) { previousTick = tick; ordinal = 0; }
                    return ordinal++;
                }
                if (matchJournal is IAirAssignmentJournal slots)
                    domain.AirAssignmentRecorded = assignment =>
                    {
                        try { slots.AirAssignment(assignment); }
                        catch (Exception error) { JournalProblem(error); }
                    };
                if (matchJournal is IAcceptedFlightJournal flight)
                {
                    domain.FlightDecisionAccepted = (owner, id, stamp, position, destination, target, moving, held) =>
                    {
                        long tick = domain.Tick;
                        try { flight.AcceptedFlight(owner, tick, DecisionOrdinal(tick), id, stamp, position, destination, target, moving, held); }
                        catch (Exception error) { JournalProblem(error); }
                    };
                }
                if (matchJournal is IAssignedGroundSlotJournal slotsJournal)
                    domain.Navigation.RouteRequested += request =>
                    {
                        string owner = domain.RouteJournalOwner(request.Entity);
                        if (owner == null || !request.GoalLocation.HasValue) return;
                        long tick = domain.Tick;
                        try
                        {
                            slotsJournal.GroundSlot(owner, tick, DecisionOrdinal(tick), request.Session, request.Entity,
                                request.Request, request.Order, request.GoalLocation.Value);
                        }
                        catch (Exception error) { JournalProblem(error); }
                    };
                if (matchJournal is IAcceptedRouteJournal routes)
                {
                    domain.Navigation.ReadyRouteAccepted += answer =>
                    {
                        var request = answer.Request;
                        string owner = domain.RouteJournalOwner(request.Entity);
                        if (owner == null) return;
                        long tick = domain.Tick;
                        try
                        {
                            routes.AcceptedRoute(owner, tick, DecisionOrdinal(tick), request.Entity, request.Request, request.Order,
                                Array.AsReadOnly(answer.ReadOnlyRouteBacking), answer.Corridor);
                        }
                        catch (Exception error) { JournalProblem(error); }
                    };
                }
                if (matchJournal is IReissuedRouteJournal reissues)
                    domain.Navigation.ReadyRouteReissued += answer =>
                    {
                        var request = answer.Request;
                        string owner = domain.RouteJournalOwner(request.Entity);
                        if (owner == null) return;
                        long tick = domain.Tick;
                        try
                        {
                            reissues.ReissuedRoute(owner, tick, DecisionOrdinal(tick), request.Entity, request.Request,
                                request.Order, Array.AsReadOnly(answer.ReadOnlyRouteBacking), answer.Corridor);
                        }
                        catch (Exception error) { JournalProblem(error); }
                    };
                if (matchJournal is IFailedGroundRouteJournal groundFailures)
                    domain.Navigation.GroundRouteFailed += (answer, outcome) =>
                    {
                        string owner = domain.RouteJournalOwner(answer.Request.Entity);
                        if (owner == null) return;
                        long tick = domain.Tick;
                        try { groundFailures.FailedGroundRoute(owner, tick, DecisionOrdinal(tick), answer, outcome); }
                        catch (Exception error) { JournalProblem(error); }
                    };
                if(matchJournal is IAiDecisionJournal timing)
                    aiScheduler.ReviewTelemetry=(owner,source,tick,status,phases)=>{
                        try { timing.DecisionTiming(owner,source,tick,status,phases); }
                        catch(Exception error){JournalProblem(error);}
                    };
            }
            catch(Exception error){JournalProblem(error);}
        }
        void JournalProblem(Exception error)
        {
            JournalFailure=error.ToString();
            if(System.Threading.Interlocked.Exchange(ref journalFailed,1)!=0)return;
            try{JournalFailureReported?.Invoke(error);}catch{/* Recorder failure cannot fail the match. */}
        }
        void JournalCapture(bool paused)
        {using var journalTiming=PerformanceProfile.Measure(ProfileStage.JournalCapture);if(matchJournal==null)return;try{matchJournal.Capture(authorityTick,paused);}catch(Exception error){JournalProblem(error);}}
        void JournalCommand(PlayableCommand command)
        {if(matchJournal==null)return;try{matchJournal.Command(command);}catch(Exception error){JournalProblem(error);}}
        void JournalReceipt(PlayableCommandReceipt receipt)
        {if(matchJournal==null)return;try{matchJournal.Receipt(receipt);}catch(Exception error){JournalProblem(error);}}
        void JournalException(Exception error)
        {if(matchJournal==null)return;try{matchJournal.Failure(error,domain.Tick);}catch(Exception secondary){JournalProblem(secondary);}}
        void JournalComplete()
        {if(matchJournal==null)return;try{matchJournal.Capture(authorityTick,paused!=0);matchJournal.Complete(latest,domain.Result);}catch(Exception error){JournalProblem(error);}}
    }
}
