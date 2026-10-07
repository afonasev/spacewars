using System;
using System.Collections.Generic;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    public enum PlayableAiMidgameStrategy { MassAssault, MapControl, Raids }

    [Serializable]
    public sealed class PlayableAiMidgameCheckpoint
    {
        public PlayableAiMidgameCheckpoint(int seed,long generation,long reviewedTick,long lastStrategyTick,long openingCompletedTick,
            int decisionSequence,PlayableAiOpeningPhase phase,PlayableAiMidgameStrategy? strategy,string ownerId,string sourceIdentity,string profileId,int profileRevision)
        {Seed=seed;Generation=generation;ReviewedTick=reviewedTick;LastStrategyTick=lastStrategyTick;OpeningCompletedTick=openingCompletedTick;
            DecisionSequence=decisionSequence;Phase=phase;Strategy=strategy;OwnerId=ownerId;SourceIdentity=sourceIdentity;ProfileId=profileId;ProfileRevision=profileRevision;}
        public int Seed{get;} public long Generation{get;} public long ReviewedTick{get;} public long LastStrategyTick{get;} public long OpeningCompletedTick{get;}
        public int DecisionSequence{get;} public PlayableAiOpeningPhase Phase{get;} public PlayableAiMidgameStrategy? Strategy{get;}
        public string OwnerId{get;} public string SourceIdentity{get;}
        public string ProfileId{get;} public int ProfileRevision{get;}
    }

    // Separate diagnostic policy. Frozen release:8 values are source AI rules, not new native gameplay balance.
    public sealed class PlayableAiMidgameStrategyPolicy
    {
        private static readonly PlayableAiMidgameStrategy[] Ordered={PlayableAiMidgameStrategy.MapControl,PlayableAiMidgameStrategy.MassAssault,PlayableAiMidgameStrategy.Raids};
        private readonly string OwnerId;
        private PlayableProfile profile;
        internal void Rebind(PlayableProfile next){profile=next;if(checkpoint!=null){var c=checkpoint;checkpoint=new PlayableAiMidgameCheckpoint(c.Seed,c.Generation,c.ReviewedTick,c.LastStrategyTick,c.OpeningCompletedTick,c.DecisionSequence,c.Phase,c.Strategy,c.OwnerId,c.SourceIdentity,next.ProfileId,next.Revision);}}
        private PlayableAiMidgameCheckpoint checkpoint;
        public PlayableAiMidgameStrategyPolicy(PlayableProfile profile,string ownerId="player-1"){this.profile=profile??throw new ArgumentNullException(nameof(profile));OwnerId=ownerId;}
        public PlayableAiMidgameCheckpoint Capture()=>checkpoint;
        public void Restore(PlayableAiMidgameCheckpoint state)
        {
            if(state!=null&&(state.OwnerId!=OwnerId||state.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||
                state.ProfileId!=profile.ProfileId||state.ProfileRevision!=profile.Revision||
                state.DecisionSequence<0||state.ReviewedTick<0||state.OpeningCompletedTick<0))throw new ArgumentException("Strategy checkpoint provenance is invalid.",nameof(state));
            checkpoint=state;
        }
        public PlayableAiMidgameCheckpoint Review(PlayableAiObservation observation,PlayableAiOpeningCompositionState opening,
            PlayableAiCombatMission mission=null,bool emergency=false)
        {
            if(observation==null||opening==null)throw new ArgumentNullException(observation==null?nameof(observation):nameof(opening));
            if(observation.OwnerId!=OwnerId||opening.OwnerId!=OwnerId||opening.MatchSeed!=observation.Seed||opening.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity)
                throw new ArgumentException("Opening and owner observation identities differ.",nameof(opening));
            if(observation.ProfileId!=profile.ProfileId||observation.ProfileRevision!=profile.Revision)
                throw new ArgumentException("Native gameplay profile differs from the policy.",nameof(observation));
            if(mission!=null&&(mission.OwnerId!=OwnerId||mission.SourceIdentity!=opening.SourceIdentity||mission.Generation!=observation.Generation))
                throw new ArgumentException("Mission is not an owner/source/generation match.",nameof(mission));
            var current=checkpoint;
            if(current==null||current.Seed!=observation.Seed||current.Generation!=observation.Generation)
                current=new PlayableAiMidgameCheckpoint(observation.Seed,observation.Generation,-1,0,0,0,PlayableAiOpeningPhase.Active,null,OwnerId,opening.SourceIdentity,observation.ProfileId,observation.ProfileRevision);
            if(observation.Tick<=current.ReviewedTick)return current;
            if(current.ReviewedTick>=0&&observation.Tick-current.ReviewedTick<45)return current; // frozen fighter decisionIntervalTicks
            var phase=current.Phase;var completed=current.OpeningCompletedTick;
            if(phase==PlayableAiOpeningPhase.Active)
            {
                if(observation.Tick>=Deadline(opening.Opening)*30){phase=PlayableAiOpeningPhase.Aborted;completed=observation.Tick;}
                else if(OpeningComplete(observation,opening.Opening,mission)){phase=PlayableAiOpeningPhase.Complete;completed=observation.Tick;}
            }
            var sequence=current.DecisionSequence+1;var strategy=current.Strategy;var strategyTick=current.LastStrategyTick;
            // Source runOpening marks the transition and returns; midgame starts on a later decision.
            if(phase!=current.Phase)return checkpoint=new PlayableAiMidgameCheckpoint(observation.Seed,observation.Generation,observation.Tick,strategyTick,completed,sequence,phase,strategy,OwnerId,opening.SourceIdentity,observation.ProfileId,observation.ProfileRevision);
            if(phase!=PlayableAiOpeningPhase.Active&&!emergency&&!VisibleCenterEmergency(observation,profile)&&observation.Tick-completed>=Commitment(opening.Opening)*30)
            {
                var utilities=Utilities(observation,opening,strategy,sequence,profile);
                var best=Ordered.OrderByDescending(x=>utilities[x]).ThenBy(x=>Name(x),StringComparer.Ordinal).First();
                var commitmentTicks=(long)Math.Ceiling(360*Trait(opening.PersonalitySeed,"commitment",.2,.9));
                var canSwitch=!strategy.HasValue||observation.Tick-strategyTick>=Math.Max(90,commitmentTicks);
                var margin=!strategy.HasValue||utilities[best]>=utilities[strategy.Value]+.18;
                if(canSwitch&&margin&&strategy!=best){strategy=best;strategyTick=observation.Tick;}
            }
            checkpoint=new PlayableAiMidgameCheckpoint(observation.Seed,observation.Generation,observation.Tick,strategyTick,completed,sequence,phase,strategy,OwnerId,opening.SourceIdentity,observation.ProfileId,observation.ProfileRevision);
            return checkpoint;
        }
        private static bool OpeningComplete(PlayableAiObservation o,PlayableAiOpening opening,PlayableAiCombatMission mission)
        {
            int buildings(PlayableBuildingKind kind)=>o.Buildings.Count(b=>b.Owner==o.Owner&&b.Kind==kind&&b.Health>0);
            int units(PlayableEntityKind kind)=>o.Entities.Count(e=>e.Owner==o.Owner&&e.Kind==kind&&e.Health>0);
            if(opening==PlayableAiOpening.Safe)return buildings(PlayableBuildingKind.Factory)>=1&&buildings(PlayableBuildingKind.Refinery)>=1;
            if(opening==PlayableAiOpening.GreedySafe)return buildings(PlayableBuildingKind.Factory)>=1&&buildings(PlayableBuildingKind.Refinery)>=2;
            if(opening==PlayableAiOpening.GreedyMine)return buildings(PlayableBuildingKind.Mine)>=1;
            if(mission==null||!mission.Deployed||mission.StartedTick>o.Tick)return false;
            if(opening==PlayableAiOpening.BlindRush)return units(PlayableEntityKind.Tank)>=1;
            if(opening==PlayableAiOpening.ExplorerAllIn)return units(PlayableEntityKind.Explorer)>=4;
            return buildings(PlayableBuildingKind.Mine)>=2&&units(PlayableEntityKind.Explorer)>=5;
        }
        private static int Deadline(PlayableAiOpening opening)=>opening==PlayableAiOpening.BlindRush?90:opening==PlayableAiOpening.ExplorerAllIn?75:
            opening==PlayableAiOpening.DoubleMineExplorerRush?150:opening==PlayableAiOpening.GreedyMine?90:opening==PlayableAiOpening.GreedySafe?135:105;
        private static int Commitment(PlayableAiOpening opening)=>opening==PlayableAiOpening.BlindRush?45:opening==PlayableAiOpening.ExplorerAllIn?55:
            opening==PlayableAiOpening.DoubleMineExplorerRush?70:0;
        private static bool VisibleCenterEmergency(PlayableAiObservation observation,PlayableProfile profile)
        {
            var centers=observation.Buildings.Where(b=>b.Owner==observation.Owner&&b.Health>0&&
                (b.Kind==PlayableBuildingKind.Headquarters||b.Kind==PlayableBuildingKind.Outpost)).ToArray();
            var enemies=observation.Entities.Where(e=>e.Owner!=observation.Owner&&e.Health>0).ToArray();
            if(!enemies.Any(e=>centers.Any(c=>Distance(e.Position,c.Position)<=30)))return false;
            double force(IEnumerable<PlayableEntitySnapshot> units)=>units.Sum(e=>(e.Kind==PlayableEntityKind.Explorer?.55:1)*Math.Min(1,e.Health/(double)(e.Kind==PlayableEntityKind.Explorer?profile.ExplorerHealth:e.Kind==PlayableEntityKind.Shkval?profile.ShkvalHealth:profile.TankHealth)));
            return force(enemies)/Math.Max(.1,force(observation.Entities.Where(e=>e.Owner==observation.Owner&&e.Health>0)))>=.75;
        }
        private static double Distance(NavPoint a,NavPoint b){var x=a.X-b.X;var z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
        private static Dictionary<PlayableAiMidgameStrategy,double> Utilities(PlayableAiObservation o,PlayableAiOpeningCompositionState opening,PlayableAiMidgameStrategy? previous,int sequence,PlayableProfile profile)
        {
            var seed=opening.PersonalitySeed;
            var own=o.Entities.Where(e=>e.Owner==o.Owner&&e.Health>0).ToArray();
            var enemy=o.Entities.Where(e=>e.Owner!=o.Owner&&e.Health>0).ToArray();
            // Native health-weighted values approximate source threat; no hidden enemies are consulted.
            double force(IEnumerable<PlayableEntitySnapshot> units)=>units.Sum(e=>(e.Kind==PlayableEntityKind.Explorer?.55:1)*Math.Min(1,e.Health/(double)(e.Kind==PlayableEntityKind.Explorer?profile.ExplorerHealth:e.Kind==PlayableEntityKind.Shkval?profile.ShkvalHealth:profile.TankHealth)));
            var enemyForce=force(enemy);var advantage=enemyForce>0?Math.Min(3,force(own)/Math.Max(.1,enemyForce)):1;
            var opportunities=o.Sites.Count(s=>s.Owner==null&&(s.Site.Kind==PlayableBuildingKind.Mine||s.Site.Kind==PlayableBuildingKind.Outpost));
            var mapOpportunity=Math.Min(1,opportunities/3d);
            var vulnerable=o.Buildings.Count(b=>b.Owner!=o.Owner&&b.Health>0&&(b.Kind==PlayableBuildingKind.Mine||b.Kind==PlayableBuildingKind.Refinery));
            var openingWeights=Readiness(opening.InitialOpening);
            var result=new Dictionary<PlayableAiMidgameStrategy,double>{
                {PlayableAiMidgameStrategy.MassAssault,.36*openingWeights[0]+Math.Max(Trait(seed,"aggression",.15,.95),Trait(seed,"allIn",.05,.65))+Math.Max(0,advantage-1)*.8},
                {PlayableAiMidgameStrategy.MapControl,.42*openingWeights[1]+Math.Max(Trait(seed,"greed",.15,.95),Math.Max(Trait(seed,"caution",.15,.95),Trait(seed,"remoteExpansion",.05,.65)))+mapOpportunity*.65},
                {PlayableAiMidgameStrategy.Raids,.22*openingWeights[2]+Math.Max(Trait(seed,"flanking",.1,.9),Math.Max(Trait(seed,"aggression",.15,.95),Trait(seed,"scouting",.35,1)))+vulnerable*.9}
            };
            foreach(var kind in Ordered)
            {
                if(previous.HasValue&&kind!=previous.Value)result[kind]-=kind==PlayableAiMidgameStrategy.MassAssault?.12:kind==PlayableAiMidgameStrategy.MapControl?.08:.1;
                result[kind]+=(Random(seed,$"midgame:{sequence}:{Name(kind)}")-.5)*.12; // fighter estimateNoise
            }
            return result;
        }
        private static double[] Readiness(PlayableAiOpening opening)
        {
            switch(opening){case PlayableAiOpening.BlindRush:return new[]{1.1,.35,.7};case PlayableAiOpening.DoubleMineExplorerRush:return new[]{.85,.95,1.1};
                case PlayableAiOpening.ExplorerAllIn:return new[]{1.15,.25,1.05};case PlayableAiOpening.GreedyMine:return new[]{.6,1.05,.8};
                case PlayableAiOpening.GreedySafe:return new[]{.65,1,.75};default:return new[]{.55,.8,.7};}
        }
        public static string Name(PlayableAiMidgameStrategy strategy)=>strategy==PlayableAiMidgameStrategy.MassAssault?"mass-assault":strategy==PlayableAiMidgameStrategy.MapControl?"map-control":"raids";
        private static double Trait(uint seed,string name,double min,double max)=>min+(max-min)*Random(seed,"trait:"+name);
        private static double Random(uint seed,string key)
        {unchecked{uint value=2166136261;foreach(var character in seed+":"+key)value=(value^(ushort)character)*16777619;return value/4294967296d;}}
    }
}
