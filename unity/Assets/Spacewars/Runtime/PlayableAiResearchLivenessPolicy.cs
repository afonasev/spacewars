using System;
using System.Linq;
using Spacewars.Simulation;
namespace Spacewars.Runtime
{
    public sealed class PlayableAiResearchPolicyProfile
    {
        public const string DefaultId="adaptive-strategic-ai-v1@release";
        public PlayableAiResearchPolicyProfile(string id,int revision,int rescoutSeconds,double maximumDanger,double advantageRatio,int minimumRefineries,int defensiveReserveUnits)
        {if(String.IsNullOrWhiteSpace(id)||revision<1||rescoutSeconds<1||maximumDanger<0||advantageRatio<1||minimumRefineries<1||defensiveReserveUnits<0)throw new ArgumentOutOfRangeException(nameof(revision));Id=id;Revision=revision;RescoutSeconds=rescoutSeconds;MaximumDanger=maximumDanger;AdvantageRatio=advantageRatio;MinimumRefineries=minimumRefineries;DefensiveReserveUnits=defensiveReserveUnits;}
        public string Id{get;} public int Revision{get;} public int RescoutSeconds{get;} public double MaximumDanger{get;} public double AdvantageRatio{get;} public int MinimumRefineries{get;} public int DefensiveReserveUnits{get;}
        public static PlayableAiResearchPolicyProfile Release=>new PlayableAiResearchPolicyProfile(DefaultId,1,35,.5,1.5,2,1);
    }
    public sealed class PlayableAiResearchPolicyField
    {
        public PlayableAiResearchPolicyField(string path,string group,string label,string unit,double minimum,double maximum,double step){Path=path;Group=group;Label=label;Unit=unit;Minimum=minimum;Maximum=maximum;Step=step;}
        public string Path{get;} public string Group{get;} public string Label{get;} public string Unit{get;} public double Minimum{get;} public double Maximum{get;} public double Step{get;}
    }
    public static class PlayableAiResearchPolicyMetadata
    {
        public static readonly PlayableAiResearchPolicyField[] Fields={
            new PlayableAiResearchPolicyField("ai.scouting.rescoutSec","AI / research readiness","Scout freshness","s",1,180,1),
            new PlayableAiResearchPolicyField("ai.economy.techMaximumDanger","AI / research readiness","Maximum danger","ratio",0,3,.05),
            new PlayableAiResearchPolicyField("ai.economy.techAdvantageRatio","AI / research readiness","Force advantage","ratio",1,5,.05),
            new PlayableAiResearchPolicyField("ai.economy.techMinimumRefineries","AI / research readiness","Minimum refineries","buildings",1,8,1),
            new PlayableAiResearchPolicyField("ai.midgame.defensiveReserveUnits","AI / research readiness","Defensive reserve","units",0,100,1)
        };
    }
    [Serializable] public sealed class PlayableAiResearchReadiness
    {
        public PlayableAiResearchReadiness(long generation,long observedTick,long lastScoutTick,bool emergency,int tankCompositionTarget,double danger,double enemyForce,double forceAdvantage){Generation=generation;ObservedTick=observedTick;LastScoutTick=lastScoutTick;Emergency=emergency;TankCompositionTarget=tankCompositionTarget;Danger=danger;EnemyForce=enemyForce;ForceAdvantage=forceAdvantage;}
        public long Generation{get;} public long ObservedTick{get;} public long LastScoutTick{get;} public bool Emergency{get;} public int TankCompositionTarget{get;} public double Danger{get;} public double EnemyForce{get;} public double ForceAdvantage{get;}
        public bool Fresh(PlayableAiResearchPolicyProfile profile)=>LastScoutTick>=0&&ObservedTick-LastScoutTick<=profile.RescoutSeconds*30L;
    }
    [Serializable] public sealed class PlayableAiResearchStrategicIntent
    {
        public PlayableAiResearchStrategicIntent(long generation,long lastScoutTick,bool emergency,int tankCompositionTarget){Generation=generation;LastScoutTick=lastScoutTick;Emergency=emergency;TankCompositionTarget=tankCompositionTarget;}
        public long Generation{get;} public long LastScoutTick{get;} public bool Emergency{get;} public int TankCompositionTarget{get;}
    }
    // Stateful but serializable-by-value observer of the already published view.
    public sealed class PlayableAiResearchReadinessTracker
    {
        public PlayableAiResearchReadiness Observe(PlayableAiObservation observation,PlayableAiResearchStrategicIntent intent)
        {
            if(observation==null)throw new ArgumentNullException(nameof(observation));
            if(intent==null||intent.Generation!=observation.Generation)return new PlayableAiResearchReadiness(observation.Generation,observation.Tick,-1,false,0,0,0,1);
            var own=Math.Max(1,observation.Entities.Count(x=>x.Owner==observation.Owner));var enemy=observation.Entities.Count(x=>observation.IsHostile(x.Owner));
            double danger=enemy/(double)own,advantage=enemy==0?1.25:Math.Min(3,own/(double)enemy);
            return new PlayableAiResearchReadiness(observation.Generation,observation.Tick,intent.LastScoutTick,intent.Emergency,intent.TankCompositionTarget,danger,enemy,advantage);
        }
    }
    public sealed partial class PlayableAiResearchLivenessPolicy
    {
        internal PlayableAiResearchLivenessPolicy Fork() => (PlayableAiResearchLivenessPolicy)MemberwiseClone();

        private readonly PlayableAiResearchPolicyProfile profile;private readonly string ownerId;private long nextActionId,pendingActionId,pendingGeneration;
        public PlayableAiResearchLivenessPolicy(PlayableAiResearchPolicyProfile profile=null,long firstActionId=1,string ownerId="player-1"){this.profile=profile??PlayableAiResearchPolicyProfile.Release;this.ownerId=ownerId;if(firstActionId<1)throw new ArgumentOutOfRangeException(nameof(firstActionId));nextActionId=firstActionId;}
        public bool HasPendingObligation=>pendingActionId!=0;
        public PlayableAiAction TryPlan(PlayableAiObservation observation,PlayableAiResearchReadiness readiness)
        {
            if(observation==null||readiness==null)throw new ArgumentNullException(observation==null?nameof(observation):nameof(readiness));if(observation.OwnerId!=ownerId)throw new ArgumentException("Policy owner differs from observation.",nameof(observation));if(HasPendingObligation&&pendingGeneration!=observation.Generation)Clear();if(HasPendingObligation)return null;
            bool safe=readiness.Fresh(profile)&&readiness.Danger<=profile.MaximumDanger;
            bool advantaged=readiness.EnemyForce>0&&readiness.ForceAdvantage>=profile.AdvantageRatio;
            if(readiness.Generation!=observation.Generation||readiness.Emergency||readiness.TankCompositionTarget<=0||(!safe&&!advantaged))return null;
            var tanks=observation.Entities.Count(x=>x.Owner==observation.Owner&&x.Kind==PlayableEntityKind.Tank&&x.Health>0);var refineries=observation.Buildings.Count(x=>x.Owner==observation.Owner&&x.Kind==PlayableBuildingKind.Refinery&&x.Phase==ConstructionPhase.Ready);
            var chassis=observation.ResearchAvailability.FirstOrDefault(x=>x.Kind==PlayableResearchKind.TankChassis);
            var center=observation.Buildings.Where(x=>x.Owner==observation.Owner&&x.Kind==PlayableBuildingKind.ScientificCenter&&x.Phase==ConstructionPhase.Ready&&x.PrivateState!=null&&!x.PrivateState.Research.Any(r=>r.Kind==PlayableResearchKind.TankChassis)).OrderBy(x=>x.Id).FirstOrDefault();
            if(chassis==null||!chassis.Available||Double.IsNaN(chassis.Cost)||Double.IsInfinity(chassis.Cost)||chassis.Cost<0||observation.Credits<chassis.Cost||tanks==0||tanks<profile.DefensiveReserveUnits||refineries<profile.MinimumRefineries||center==null)return null;pendingActionId=nextActionId++;pendingGeneration=observation.Generation;
            return new PlayableAiAction(pendingActionId,observation.OwnerId,observation.ProfileId,observation.ProfileRevision,observation.Generation,observation.SnapshotSequence,PlayableCommandKind.QueueResearch,new[]{center.Id},researchKind:PlayableResearchKind.TankChassis,seed:observation.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);
        }
        public void ObserveReceipt(PlayableAiTraceRecord record){if(record==null||!AiStateWire.CallbackGenerationMatches(record,pendingGeneration)||record.OwnerId!=ownerId||record.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||!HasPendingObligation||record.ActionId!=pendingActionId)return;if(record.Status==PlayableAiDeliveryStatus.Applied||record.Status==PlayableAiDeliveryStatus.Rejected||record.Status==PlayableAiDeliveryStatus.Stale||record.Status==PlayableAiDeliveryStatus.Cancelled||record.Status==PlayableAiDeliveryStatus.Stopped||record.Status==PlayableAiDeliveryStatus.InvalidAction||record.Status==PlayableAiDeliveryStatus.InvalidOwner)Clear();}
        private void Clear(){pendingActionId=0;pendingGeneration=0;}
    }
}
