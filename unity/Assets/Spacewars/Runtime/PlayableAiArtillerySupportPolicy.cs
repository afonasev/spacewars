using System;
using System.Linq;
using Spacewars.Simulation;

namespace Spacewars.Runtime
{
    [Serializable]
    public sealed class PlayableAiArtilleryState
    {
        public PlayableAiArtilleryState(int seed,long generation,bool earlyAdopted,int earlyOrdinal,int earlyCount,bool fulfilled,
            bool adopted,double share,string context,long reviewedTick,long holdUntilTick,int heldUnitId,NavPoint heldPosition,string sourceIdentity,string ownerId="player-1")
        {Seed=seed;Generation=generation;EarlyAdopted=earlyAdopted;EarlyOrdinal=earlyOrdinal;EarlyCount=earlyCount;Fulfilled=fulfilled;
            Adopted=adopted;Share=share;Context=context;ReviewedTick=reviewedTick;HoldUntilTick=holdUntilTick;HeldUnitId=heldUnitId;HeldPosition=heldPosition;SourceIdentity=sourceIdentity;OwnerId=ownerId;}
        public int Seed{get;} public long Generation{get;} public bool EarlyAdopted{get;} public int EarlyOrdinal{get;} public int EarlyCount{get;} public bool Fulfilled{get;}
        public bool Adopted{get;} public double Share{get;} public string Context{get;} public long ReviewedTick{get;} public long HoldUntilTick{get;} public int HeldUnitId{get;} public NavPoint HeldPosition{get;} public string SourceIdentity{get;} public string OwnerId{get;}
    }

    // Source-aligned doctrine and support micro, used by the local owner loop and diagnostics.
    public sealed class PlayableAiArtillerySupportPolicy
    {
        private readonly string OwnerId;
        private readonly PlayableProfile profile;
        private long nextActionId,pendingActionId,pendingGeneration;
        private int seed,earlyOrdinal,earlyCount,heldUnitId;
        private bool initialized,earlyAdopted,fulfilled,adopted;
        private double share;
        private long generation,holdUntilTick,reviewedTick;
        private string context;
        private NavPoint heldPosition;
        public PlayableAiArtillerySupportPolicy(PlayableProfile profile,long firstActionId=1,string ownerId="player-1")
        {this.profile=profile??throw new ArgumentNullException(nameof(profile));OwnerId=ownerId;if(firstActionId<1)throw new ArgumentOutOfRangeException(nameof(firstActionId));nextActionId=firstActionId;}
        public PlayableAiArtilleryState Capture()=>initialized?new PlayableAiArtilleryState(seed,generation,earlyAdopted,earlyOrdinal,earlyCount,fulfilled,adopted,share,context,reviewedTick,holdUntilTick,heldUnitId,heldPosition,PlayableAiOpeningComposition.SourceIdentity,OwnerId):null;
        public void Restore(PlayableAiArtilleryState state)
        {
            if(state!=null&&(state.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||state.OwnerId!=OwnerId))throw new ArgumentException("Frozen source or owner mismatch.",nameof(state));
            initialized=state!=null;seed=state?.Seed??0;generation=state?.Generation??0;earlyAdopted=state?.EarlyAdopted??false;
            earlyOrdinal=state?.EarlyOrdinal??0;earlyCount=state?.EarlyCount??0;fulfilled=state?.Fulfilled??false;adopted=state?.Adopted??false;
            share=state?.Share??0;holdUntilTick=state?.HoldUntilTick??0;heldUnitId=state?.HeldUnitId??0;heldPosition=state?.HeldPosition??default(NavPoint);
            context=state?.Context;reviewedTick=state?.ReviewedTick??0;
            pendingActionId=0;pendingGeneration=0;
        }
        public long PendingActionId=>pendingActionId;
        public PlayableAiAction TryPlan(PlayableAiObservation observation,PlayableAiOpeningCompositionState opening)
            =>TryPlan(observation,opening,null);
        public PlayableAiAction TryPlan(PlayableAiObservation observation,PlayableAiOpeningCompositionState opening,PlayableAiMidgameCheckpoint strategy)
        {
            if(observation==null||opening==null)throw new ArgumentNullException(observation==null?nameof(observation):nameof(opening));
            if(observation.OwnerId!=OwnerId||opening.OwnerId!=OwnerId||opening.MatchSeed!=observation.Seed||opening.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity)
                throw new ArgumentException("Opening identity mismatch.",nameof(opening));
            if(strategy!=null&&(strategy.OwnerId!=OwnerId||strategy.SourceIdentity!=opening.SourceIdentity||strategy.Seed!=observation.Seed||strategy.Generation!=observation.Generation||
                strategy.ProfileId!=observation.ProfileId||strategy.ProfileRevision!=observation.ProfileRevision||strategy.ReviewedTick>observation.Tick))
                throw new ArgumentException("Midgame strategy provenance does not match this owner observation.",nameof(strategy));
            if(!initialized||generation!=observation.Generation||seed!=observation.Seed)Initialize(observation,opening);
            ReviewDoctrine(observation,opening,strategy);
            if(pendingActionId!=0&&pendingGeneration!=observation.Generation)pendingActionId=0;
            if(pendingActionId!=0)return null;
            var own=observation.Entities.Where(e=>e.Owner==observation.Owner&&e.Health>0).ToArray();
            var queued=observation.Buildings.Where(b=>b.Owner==observation.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.PrivateState!=null)
                .SelectMany(b=>b.PrivateState.Orders).Select(o=>o.Kind).ToArray();
            var guns=own.Where(e=>e.Kind==PlayableEntityKind.Shkval).OrderBy(e=>e.Id).ToArray();
            if(guns.Length>0||queued.Contains(PlayableEntityKind.Shkval))fulfilled=true;
            if(earlyAdopted&&!fulfilled)earlyCount=Math.Max(earlyCount,own.Length+queued.Length);
            foreach(var gun in guns)
            {
                var fact=observation.ArtillerySupport.FirstOrDefault(a=>a.UnitId==gun.Id&&a.Generation==observation.Generation&&a.Tick==observation.Tick);
                if(fact==null)continue;
                if(!fact.Threatened&&fact.Supported&&fact.UsefulShot)
                {
                    if(gun.CurrentOrder!=null)return Action(observation,PlayableCommandKind.Stop,new[]{gun.Id});
                    continue;
                }
                if(!fact.Threatened&&heldUnitId==gun.Id&&observation.Tick<holdUntilTick&&Distance(gun.Position,heldPosition)<=1.5)
                {
                    if(gun.CurrentOrder!=null)return Action(observation,PlayableCommandKind.Stop,new[]{gun.Id});
                    continue;
                }
                if(!fact.Position.HasValue)
                {
                    if(gun.CurrentOrder!=null)return Action(observation,PlayableCommandKind.Stop,new[]{gun.Id});
                    continue;
                }
                var position=fact.Position.Value;
                if(Distance(gun.Position,position)<=1.5)
                {
                    heldUnitId=gun.Id;heldPosition=position;holdUntilTick=observation.Tick+(long)Math.Ceiling(Math.Max(5,profile.ShkvalStopForMs/1000)*30);
                    if(gun.CurrentOrder!=null)return Action(observation,PlayableCommandKind.Stop,new[]{gun.Id});
                    continue;
                }
                if(gun.CurrentOrder?.Kind==PlayableTacticalOrderKind.Move&&Distance(gun.CurrentOrder.Destination,position)<=1.5)continue;
                heldUnitId=gun.Id;heldPosition=position;holdUntilTick=observation.Tick+(long)Math.Ceiling(Math.Max(5,profile.ShkvalStopForMs/1000)*30);
                return Action(observation,PlayableCommandKind.Move,new[]{gun.Id},position);
            }
            // Frozen release doctrine uses the committed midgame strategy when it exists.
            var cover=own.Count(e=>e.Kind!=PlayableEntityKind.Shkval)+queued.Count(k=>k!=PlayableEntityKind.Shkval);
            var target=adopted?(int)Math.Floor(cover*share/(1-share)):0;
            if(earlyAdopted&&!fulfilled)target=earlyCount>=earlyOrdinal-1?1:0;
            if(target<=guns.Length+queued.Count(k=>k==PlayableEntityKind.Shkval)||observation.Population==null||
                observation.Population.Living+observation.Population.Reserved>=observation.Population.Capacity||observation.Credits<profile.ShkvalCreditCost)return null;
            var factory=observation.Buildings.Where(b=>b.Owner==observation.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.Phase==ConstructionPhase.Ready&&b.PrivateState!=null)
                .OrderBy(b=>b.PrivateState.QueueCount).ThenBy(b=>b.Id).FirstOrDefault();
            return factory==null?null:Action(observation,PlayableCommandKind.QueueShkval,new[]{factory.Id});
        }
        public void ObserveReceipt(PlayableAiTraceRecord record)
        {
            if(record==null||record.OwnerId!=OwnerId||record.SourceIdentity!=PlayableAiOpeningComposition.SourceIdentity||record.ActionId!=pendingActionId)return;
            if(record.Status!=PlayableAiDeliveryStatus.Scheduled&&record.Status!=PlayableAiDeliveryStatus.Accepted)
            {pendingActionId=0;pendingGeneration=0;}
        }
        private void Initialize(PlayableAiObservation observation,PlayableAiOpeningCompositionState opening)
        {
            initialized=true;seed=observation.Seed;generation=observation.Generation;pendingActionId=0;heldUnitId=0;holdUntilTick=0;fulfilled=false;
            earlyAdopted=Roll(opening.PersonalitySeed,"early")<.4;
            earlyOrdinal=5+(int)Math.Floor(Roll(opening.PersonalitySeed,"early:ordinal")*2);
            earlyCount=observation.Entities.Count(e=>e.Owner==observation.Owner&&e.Health>0)+
                observation.Buildings.Where(b=>b.Owner==observation.Owner&&b.Kind==PlayableBuildingKind.Factory&&b.PrivateState!=null).Sum(b=>b.PrivateState.Orders.Count);
            context=null;reviewedTick=observation.Tick;
        }
        private void ReviewDoctrine(PlayableAiObservation observation,PlayableAiOpeningCompositionState opening,PlayableAiMidgameCheckpoint midgame)
        {
            var strategy=midgame!=null&&midgame.Strategy.HasValue?PlayableAiMidgameStrategyPolicy.Name(midgame.Strategy.Value):"opening:"+OpeningName(opening.Opening);
            var large=observation.Population!=null&&observation.Population.Capacity>40;
            var enemies=observation.Entities.Where(e=>e.Owner!=observation.Owner&&e.Health>0).ToArray();
            var mass=enemies.Any(e=>enemies.Count(other=>Distance(e.Position,other.Position)<=8)>=6)||
                strategy=="mass-assault"&&observation.Entities.Count(e=>e.Owner==observation.Owner&&e.Kind!=PlayableEntityKind.Shkval&&e.Health>0)>=6;
            var next=strategy+":"+large+":"+mass;
            if(context!=null&&(context==next||observation.Tick-reviewedTick<900))return;
            context=next;reviewedTick=observation.Tick;
            adopted=Roll(opening.PersonalitySeed,strategy)<Math.Min(1,(large ? .9 : .2)+(mass ? .08 : 0))*(strategy=="raids"&&!mass?.25:1);
            share=Math.Max(0,Math.Min(.5,.2+(Roll(opening.PersonalitySeed,strategy+":share")*2-1)*.03));
        }
        private PlayableAiAction Action(PlayableAiObservation o,PlayableCommandKind kind,int[] ids,NavPoint target=default(NavPoint))
        {pendingActionId=nextActionId++;pendingGeneration=o.Generation;return new PlayableAiAction(pendingActionId,OwnerId,o.ProfileId,o.ProfileRevision,o.Generation,o.SnapshotSequence,kind,ids,target,seed:o.Seed,sourceIdentity:PlayableAiOpeningComposition.SourceIdentity);}
        private static string OpeningName(PlayableAiOpening opening)=>opening==PlayableAiOpening.GreedySafe?"greedy-safe":opening==PlayableAiOpening.GreedyMine?"greedy-mine":
            opening==PlayableAiOpening.BlindRush?"blind-rush":opening==PlayableAiOpening.ExplorerAllIn?"explorer-all-in":opening==PlayableAiOpening.DoubleMineExplorerRush?"double-mine-explorer-rush":"safe";
        private static double Roll(uint seed,string key)
        {unchecked{uint value=2166136261;foreach(var character in seed+":artillery:"+key)value=(value^(ushort)character)*16777619;return value/4294967296d;}}
        private static double Distance(NavPoint a,NavPoint b){var x=a.X-b.X;var z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
    }
}
