using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Spacewars.Simulation;
using Spacewars.Simulation.Ai;
using static Spacewars.Runtime.WorldWire;
namespace Spacewars.Runtime
{
    // Private authority wire, explicit schema. Never included in an owner snapshot.
    internal static class AiStateWire
    {
        internal const int Version=11;
        internal static bool CallbackGenerationMatches(PlayableAiTraceRecord record,long generation)
        {
            if(record.ReceiptIdentity!=null)return record.ReceiptIdentity.Generation==generation;
            var parts=(record.ObservationIdentity??"").Split(':');
            return parts.Length<7||!parts[0].StartsWith("observation-",StringComparison.Ordinal)||
                long.TryParse(parts[parts.Length-4],out var bound)&&bound==generation;
        }
        internal static void Require(bool value,string reason){if(!value)throw new ArgumentException("Invalid AI state: "+reason);}
        internal static void Write(BinaryWriter w,PlayableAiAction v){w.Write(v!=null);if(v==null)return;
            w.Write(v.ActionId);
            String(w,v.PlayerId);
            String(w,v.ProfileId);
            w.Write(v.ProfileRevision);
            w.Write(v.Generation);
            w.Write(v.SnapshotSequence);
            w.Write((int)v.Kind);
            WorldWire.Array(w,v.CopyEntityIds(),x=>{w.Write(x);});
            WorldWire.Write(w,v.Target);
            w.Write(v.SiteId);
            w.Write(v.SlotId);
            w.Write((int)v.BuildingKind);
            w.Write(v.ParentId);
            w.Write(v.TargetId);
            w.Write(v.ProductionOrderId);
            w.Write((int)v.UnitKind);
            w.Write((int)v.ResearchKind);
            w.Write(v.Seed);
            String(w,v.SourceIdentity);
        }
        internal static PlayableAiAction ReadPlayableAiAction(BinaryReader r)=>Boolean(r)?new PlayableAiAction(r.ReadInt64(),String(r),String(r),r.ReadInt32(),r.ReadInt64(),r.ReadInt64(),EnumValue<PlayableCommandKind>(r),WorldWire.Array(r,()=>r.ReadInt32()),ReadPoint(r),r.ReadInt32(),r.ReadInt32(),EnumValue<PlayableBuildingKind>(r),r.ReadInt32(),r.ReadInt32(),r.ReadInt64(),EnumValue<PlayableEntityKind>(r),EnumValue<PlayableResearchKind>(r),r.ReadInt32(),String(r)):null;
        internal static void Write(BinaryWriter w,PlayableAiTraceRecord v){w.Write(v!=null);if(v==null)return;
            String(w,v.ObservationIdentity);
            w.Write(v.ActionId);
            w.Write(v.DueTick);
            w.Write(v.CommandSequence);
            w.Write(v.ApplicationTick);
            w.Write((int)v.Status);
            w.Write(v.RuntimeStatus.HasValue);if(v.RuntimeStatus.HasValue){w.Write((int)v.RuntimeStatus.Value);}
            String(w,v.Message);
            String(w,v.OwnerId);
            String(w,v.SourceIdentity);
            Write(w,v.ReceiptIdentity);
            String(w,v.Policy);
            w.Write(v.Kind.HasValue);if(v.Kind.HasValue){w.Write((int)v.Kind.Value);}
        }
        internal static PlayableAiTraceRecord ReadPlayableAiTraceRecord(BinaryReader r)=>Boolean(r)?new PlayableAiTraceRecord(String(r),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),EnumValue<PlayableAiDeliveryStatus>(r),Boolean(r)?(PlayableCommandStatus?)EnumValue<PlayableCommandStatus>(r):null,String(r),String(r),String(r),ReadAiReceiptIdentity(r),String(r),Boolean(r)?(PlayableCommandKind?)EnumValue<PlayableCommandKind>(r):null):null;
        internal static void Write(BinaryWriter w,PlayableAiCompositionIntent v){w.Write(v!=null);if(v==null)return;
            w.Write(v.Explorer);
            w.Write(v.Tank);
            Number(w,v.ExplorerShare);
        }
        internal static PlayableAiCompositionIntent ReadPlayableAiCompositionIntent(BinaryReader r)=>Boolean(r)?new PlayableAiCompositionIntent(r.ReadInt32(),r.ReadInt32(),Number(r)):null;
        internal static void Write(BinaryWriter w,PlayableAiOpeningCompositionState v){w.Write(v!=null);if(v==null)return;
            String(w,v.OwnerId);
            w.Write(v.MatchSeed);
            String(w,v.ProfileIdentity);
            String(w,v.SourceIdentity);
            w.Write(v.PersonalitySeed);
            w.Write((int)v.Opening);
            w.Write((int)v.Phase);
            Write(w,v.Intent);
            WorldWire.Array(w,v.Commitments.ToArray(),x=>{String(w,x);});
        }
        internal static PlayableAiOpeningCompositionState ReadPlayableAiOpeningCompositionState(BinaryReader r)=>Boolean(r)?new PlayableAiOpeningCompositionState(String(r),r.ReadInt32(),String(r),String(r),r.ReadUInt32(),EnumValue<PlayableAiOpening>(r),EnumValue<PlayableAiOpeningPhase>(r),ReadPlayableAiCompositionIntent(r),WorldWire.Array(r,()=>String(r))):null;
        internal static void Write(BinaryWriter w,PlayableAiCombatMission v){w.Write(v!=null);if(v==null)return;
            w.Write(v.Generation);
            w.Write(v.StartedTick);
            w.Write(v.ProgressTick);
            w.Write(v.ReinforcementTick);
            w.Write(v.TargetId);
            w.Write(v.PublicSiteId);
            WorldWire.Write(w,v.Target);
            w.Write(v.VisibleTarget);
            w.Write(v.Deployed);
            w.Write(v.Completed);
            String(w,v.Reason);
            WorldWire.Array(w,v.AssignedIds.ToArray(),x=>{w.Write(x);});
            String(w,v.OwnerId);
            String(w,v.SourceIdentity);
        }
        internal static PlayableAiCombatMission ReadPlayableAiCombatMission(BinaryReader r)=>Boolean(r)?new PlayableAiCombatMission(r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt32(),r.ReadInt32(),ReadPoint(r),Boolean(r),Boolean(r),Boolean(r),String(r),WorldWire.Array(r,()=>r.ReadInt32()),String(r),String(r)):null;
        internal static void Write(BinaryWriter w,PlayableAiMidgameCheckpoint v){w.Write(v!=null);if(v==null)return;
            w.Write(v.Seed);
            w.Write(v.Generation);
            w.Write(v.ReviewedTick);
            w.Write(v.LastStrategyTick);
            w.Write(v.OpeningCompletedTick);
            w.Write(v.DecisionSequence);
            w.Write((int)v.Phase);
            w.Write(v.Strategy.HasValue);if(v.Strategy.HasValue){w.Write((int)v.Strategy.Value);}
            String(w,v.OwnerId);
            String(w,v.SourceIdentity);
            String(w,v.ProfileId);
            w.Write(v.ProfileRevision);
        }
        internal static PlayableAiMidgameCheckpoint ReadPlayableAiMidgameCheckpoint(BinaryReader r)=>Boolean(r)?new PlayableAiMidgameCheckpoint(r.ReadInt32(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt64(),r.ReadInt32(),EnumValue<PlayableAiOpeningPhase>(r),Boolean(r)?(PlayableAiMidgameStrategy?)EnumValue<PlayableAiMidgameStrategy>(r):null,String(r),String(r),String(r),r.ReadInt32()):null;
        internal static void Write(BinaryWriter w,AiReceiptIdentity v){w.Write(v!=null);if(v==null)return;w.Write(v.Generation);String(w,v.OwnerId);w.Write(v.DecisionOrdinal);w.Write(v.ActionOrdinal);}
        internal static AiReceiptIdentity ReadAiReceiptIdentity(BinaryReader r)=>Boolean(r)?new AiReceiptIdentity(r.ReadInt64(),String(r),r.ReadInt64(),r.ReadInt32()):null;
    }
    public sealed partial class PlayableAiEconomicLivenessPolicy
    {
        internal void WriteState(BinaryWriter w){
            w.Write(nextActionId);
            w.Write(pendingActionId);
            w.Write(pendingGeneration);
            w.Write((int)pending);
        }
        internal void ReadState(BinaryReader r){
            nextActionId=r.ReadInt64();
            pendingActionId=r.ReadInt64();
            pendingGeneration=r.ReadInt64();
            pending=EnumValue<Obligation>(r);
            AiStateWire.Require(nextActionId>0&&pendingActionId>=0&&pendingActionId<nextActionId&&pendingGeneration>=0&&((pendingActionId==0)==(pendingGeneration==0)),"policy allocators");
        }
        internal long StatePendingId=>pendingActionId;
        internal long StatePendingGeneration=>pendingGeneration;
    }
    public sealed partial class PlayableAiProductionLivenessPolicy
    {
        internal void WriteState(BinaryWriter w){
            w.Write(nextActionId);
            w.Write(pendingActionId);
            w.Write(pendingGeneration);
        }
        internal void ReadState(BinaryReader r){
            nextActionId=r.ReadInt64();
            pendingActionId=r.ReadInt64();
            pendingGeneration=r.ReadInt64();
            AiStateWire.Require(nextActionId>0&&pendingActionId>=0&&pendingActionId<nextActionId&&pendingGeneration>=0&&((pendingActionId==0)==(pendingGeneration==0)),"policy allocators");
        }
        internal long StatePendingId=>pendingActionId;
        internal long StatePendingGeneration=>pendingGeneration;
    }
    public sealed partial class PlayableAiResearchLivenessPolicy
    {
        internal void WriteState(BinaryWriter w){
            w.Write(nextActionId);
            w.Write(pendingActionId);
            w.Write(pendingGeneration);
        }
        internal void ReadState(BinaryReader r){
            nextActionId=r.ReadInt64();
            pendingActionId=r.ReadInt64();
            pendingGeneration=r.ReadInt64();
            AiStateWire.Require(nextActionId>0&&pendingActionId>=0&&pendingActionId<nextActionId&&pendingGeneration>=0&&((pendingActionId==0)==(pendingGeneration==0)),"policy allocators");
        }
        internal long StatePendingId=>pendingActionId;
        internal long StatePendingGeneration=>pendingGeneration;
    }
    public sealed partial class PlayableAiScoutLivenessPolicy
    {
        internal void WriteState(BinaryWriter w){
            w.Write(nextActionId);
            w.Write(pendingActionId);
            w.Write(pendingGeneration);
        }
        internal void ReadState(BinaryReader r){
            nextActionId=r.ReadInt64();
            pendingActionId=r.ReadInt64();
            pendingGeneration=r.ReadInt64();
            AiStateWire.Require(nextActionId>0&&pendingActionId>=0&&pendingActionId<nextActionId&&pendingGeneration>=0&&((pendingActionId==0)==(pendingGeneration==0)),"policy allocators");
        }
        internal long StatePendingId=>pendingActionId;
        internal long StatePendingGeneration=>pendingGeneration;
    }
    public sealed partial class PlayableAiMissionDefensePolicy
    {
        internal void WriteState(BinaryWriter w){
            w.Write(nextActionId);
            w.Write(pendingActionId);
            w.Write(pendingGeneration);
            WorldWire.Array(w,pendingAdded,x=>{w.Write(x);});
            w.Write(pendingDefense);
            w.Write(preemptedActionId);
            AiStateWire.Write(w,mission);
            w.Write(double.IsPositiveInfinity(previousDistance));if(!double.IsPositiveInfinity(previousDistance))Number(w,previousDistance);
        }
        internal void ReadState(BinaryReader r){
            nextActionId=r.ReadInt64();
            pendingActionId=r.ReadInt64();
            pendingGeneration=r.ReadInt64();
            pendingAdded=WorldWire.Array(r,()=>r.ReadInt32());
            pendingDefense=Boolean(r);
            preemptedActionId=r.ReadInt64();
            mission=AiStateWire.ReadPlayableAiCombatMission(r);
            previousDistance=Boolean(r)?double.PositiveInfinity:Number(r);
            AiStateWire.Require(nextActionId>0&&pendingActionId>=0&&pendingActionId<nextActionId&&pendingGeneration>=0&&((pendingActionId==0)==(pendingGeneration==0)),"policy allocators");
        }
        internal long StatePendingId=>pendingActionId;
        internal long StatePendingGeneration=>pendingGeneration;
    }
    public sealed partial class PlayableAiMidgameStrategyPolicy
    {
        internal void WriteState(BinaryWriter w){
            AiStateWire.Write(w,checkpoint);
        }
        internal void ReadState(BinaryReader r){
            checkpoint=AiStateWire.ReadPlayableAiMidgameCheckpoint(r);
        }
    }
    public sealed partial class PlayableAiArtillerySupportPolicy
    {
        internal void WriteState(BinaryWriter w){
            w.Write(nextActionId);
            w.Write(pendingActionId);
            w.Write(pendingGeneration);
            w.Write(seed);
            w.Write(earlyOrdinal);
            w.Write(earlyCount);
            w.Write(heldUnitId);
            w.Write(initialized);
            w.Write(earlyAdopted);
            w.Write(fulfilled);
            w.Write(adopted);
            Number(w,share);
            w.Write(generation);
            w.Write(holdUntilTick);
            w.Write(reviewedTick);
            String(w,context);
            WorldWire.Write(w,heldPosition);
        }
        internal void ReadState(BinaryReader r){
            nextActionId=r.ReadInt64();
            pendingActionId=r.ReadInt64();
            pendingGeneration=r.ReadInt64();
            seed=r.ReadInt32();
            earlyOrdinal=r.ReadInt32();
            earlyCount=r.ReadInt32();
            heldUnitId=r.ReadInt32();
            initialized=Boolean(r);
            earlyAdopted=Boolean(r);
            fulfilled=Boolean(r);
            adopted=Boolean(r);
            share=Number(r);
            generation=r.ReadInt64();
            holdUntilTick=r.ReadInt64();
            reviewedTick=r.ReadInt64();
            context=String(r);
            heldPosition=ReadPoint(r);
            AiStateWire.Require(nextActionId>0&&pendingActionId>=0&&pendingActionId<nextActionId&&pendingGeneration>=0&&((pendingActionId==0)==(pendingGeneration==0)),"policy allocators");
        }
        internal long StatePendingId=>pendingActionId;
        internal long StatePendingGeneration=>pendingGeneration;
    }
}
namespace Spacewars.Runtime
{
    internal sealed partial class AiAuthorityScheduler
    {
        internal void WriteState(BinaryWriter w)
        {
            w.Write(AiStateWire.Version);w.Write(nextActionId);
            WorldWire.Array(w,owners,o=>o.WriteState(w));
        }
        internal static AiAuthorityScheduler ReadState(BinaryReader r,PlayableProfile profile,AiProfile ai,long generation,int seed,long tick,IEnumerable<string> expectedOwners)
        {
            AiStateWire.Require(r.ReadInt32()==AiStateWire.Version,"unsupported scheduler schema");
            long next=r.ReadInt64();AiStateWire.Require(next>=0,"action allocator");
            var loops=WorldWire.Array(r,()=>PlayableAiOwnerLoop.ReadState(r,profile,ai,generation,seed,tick));
            AiStateWire.Require(loops.Select(o=>o.OwnerId).SequenceEqual(expectedOwners.OrderBy(x=>x,StringComparer.Ordinal)),"AI owner roster");
            var result=new AiAuthorityScheduler(loops){nextActionId=next};
            AiStateWire.Require(loops.SelectMany(o=>o.StateActionIds).All(id=>id>0&&id<=next),"global action allocator binding");
            AiStateWire.Require(loops.SelectMany(o=>o.StatePendingActionIds).Distinct().Count()==loops.Sum(o=>o.StatePendingActionIds.Count()),"pending action uniqueness");
            return result;
        }
        internal void Rebind(AiProfile next){foreach(var owner in owners)owner.Rebind(next);}
    }
    public sealed partial class AiDecisionArbiter
    {
        internal void WriteState(BinaryWriter w)
        {
            WorldWire.Array(w,history.OrderBy(x=>x.Key,StringComparer.Ordinal).ToArray(),p=>{String(w,p.Key);w.Write(p.Value.FirstTick);w.Write(p.Value.NextRetry);w.Write(p.Value.Failures);String(w,p.Value.Conditions);});
        }
        internal void ReadState(BinaryReader r,long tick)
        {
            foreach(var p in WorldWire.Array(r,()=>new KeyValuePair<string,History>(String(r),new History{FirstTick=r.ReadInt64(),NextRetry=r.ReadInt64(),Failures=r.ReadInt32(),Conditions=String(r)})))
            {
                AiStateWire.Require(!string.IsNullOrWhiteSpace(p.Key)&&p.Value.FirstTick>=0&&p.Value.FirstTick<=tick&&p.Value.NextRetry>=0&&p.Value.Failures>=0&&!history.ContainsKey(p.Key),"arbiter history");history.Add(p.Key,p.Value);
            }
        }
    }
    internal sealed partial class PlayableAiOwnerLoop
    {
        internal IEnumerable<long> StateActionIds=>records.Select(r=>r.ActionId).Concat(StatePendingActionIds);
        internal IEnumerable<long> StatePendingActionIds=>pendingItems.Select(p=>p.Action.ActionId);
        internal void Rebind(AiProfile next)
        {
            if(next.Hash==nativeProfile.Hash)return;
            while(pending!=null)Cancel(PlayableAiDeliveryStatus.Cancelled,"AI profile revision changed.");
            nativeProfile=next;knowledge.Rebind(next);nativeConfig=new AiOwnerConfig(OwnerId,nativeConfig.Difficulty,opening.MatchSeed,next);
            DecisionIntervalTicks=AiProfile.SecondsToTicks(next.DifficultyValue(nativeConfig.Difficulty,"decisionSeconds"),30);
            ObservationDelayTicks=AiProfile.SecondsToTicks(next.DifficultyValue(nativeConfig.Difficulty,"reactionSeconds"),30);
            actionLimit=(int)next.DifficultyValue(nativeConfig.Difficulty,"actionsPerDecision");
            repeatTicks=AiProfile.SecondsToTicks(next.Value("decision.repeatOrderSeconds"),30);arbiter.Rebind(next);
            armies.Rebind(next,nativeConfig.Difficulty);armyPlanner.ReconcileRegistry(armies,authorityTick);defense.ReconcileRegistry(armies);demand.Rebind(next,authorityTick);expansion.Rebind(authorityTick,next);
            budget.RebindLifetime(AiProfile.SecondsToTicks(next.Value("economy.reservationExpirySeconds"),30));launches.RebindHorizon(budget);infrastructure.Rebind(profile,budget,authorityTick);launches.ReconcileFunding(budget,profile,authorityTick);expansion.ReconcileFunding(budget,profile,authorityTick,pendingItems.Select(x=>x.Intent));
            Checkpoint=Capture().BindNativeProfile(nativeProfile,nativeConfig);
        }
        internal void WriteState(BinaryWriter w)
        {
            String(w,nativeProfile.Id);w.Write(nativeProfile.Revision);String(w,nativeProfile.Hash);w.Write((int)nativeConfig.Difficulty);
            AiStateWire.Write(w,opening);w.Write(Generation);
            w.Write(lastDecisionTick);w.Write(lastScoutTick);w.Write(lastScoutOrderTick);w.Write(lastCommandSequence);w.Write(ordinal);w.Write(authorityTick);
            String(w,lastPolicy);w.Write(lastActionKind.HasValue);if(lastActionKind.HasValue)w.Write((int)lastActionKind.Value);
            economy.WriteState(w);production.WriteState(w);research.WriteState(w);scout.WriteState(w);mission.WriteState(w);midgame.WriteState(w);artillery.WriteState(w);arbiter.WriteState(w);
            AiBudgetStateWire.Write(w,budget);demand.WriteState(w);launches.WriteState(w);expansion.WriteState(w);infrastructure.WriteState(w);armies.WriteState(w);armyPlanner.WriteState(w);defense.WriteState(w);knowledge.WriteState(w);
            WorldWire.Array(w,records.ToArray(),rec=>AiStateWire.Write(w,rec));
            WorldWire.Array(w,pendingItems.ToArray(),p=>{
                String(w,p.ObservationIdentity);w.Write(p.ObservationTick);w.Write(p.SnapshotSequence);AiStateWire.Write(w,p.Action);
                w.Write(p.DueTick);w.Write(p.HumanSequence);w.Write(p.Ordinal);w.Write(p.LocalActionId);AiStateWire.Write(w,p.Identity);
                String(w,p.PolicyName);w.Write(p.Scout);
                var i=p.Intent;String(w,i.Id);w.Write(i.ExpiresTick);w.Write(i.Credits);w.Write(i.Population);WorldWire.Array(w,i.Claims.ToArray(),c=>String(w,c));w.Write(i.Priority);Number(w,i.Utility);String(w,i.Conditions);String(w,i.Reason);w.Write(i.StartupFund);String(w,i.ReservationId);w.Write(i.Overdue);
            });
        }
        internal static PlayableAiOwnerLoop ReadState(BinaryReader r,PlayableProfile profile,AiProfile ai,long generation,int seed,long tick)
        {
            AiStateWire.Require(String(r)==ai.Id&&r.ReadInt32()==ai.Revision&&String(r)==ai.Hash,"AI profile mismatch");
            var difficulty=EnumValue<AiDifficulty>(r);var opening=AiStateWire.ReadPlayableAiOpeningCompositionState(r);
            AiStateWire.Require(opening!=null&&opening.MatchSeed==seed&&PlayableAiOpeningComposition.HasNativeBinding(opening),"opening/RNG binding");
            AiStateWire.Require(r.ReadInt64()==generation,"owner generation");
            var o=new PlayableAiOwnerLoop(profile,opening,generation,null,ai,difficulty,true);
            o.lastDecisionTick=r.ReadInt64();o.lastScoutTick=r.ReadInt64();o.lastScoutOrderTick=r.ReadInt64();o.lastCommandSequence=r.ReadInt64();o.ordinal=r.ReadInt64();o.authorityTick=r.ReadInt64();
            o.lastPolicy=String(r);o.lastActionKind=Boolean(r)?(PlayableCommandKind?)EnumValue<PlayableCommandKind>(r):null;
            AiStateWire.Require(o.lastDecisionTick>=0&&o.lastDecisionTick<=tick&&o.lastScoutTick>=-1&&o.lastScoutTick<=tick&&o.lastScoutOrderTick>=-1&&o.lastScoutOrderTick<=tick&&o.lastCommandSequence>=0&&o.ordinal>=0&&o.authorityTick>=0&&o.authorityTick<=tick,"owner clocks/ordinals");
            o.economy.ReadState(r);o.production.ReadState(r);o.research.ReadState(r);o.scout.ReadState(r);o.mission.ReadState(r);o.midgame.ReadState(r);o.artillery.ReadState(r);o.arbiter.ReadState(r,tick);
            o.budget=AiBudgetStateWire.Read(r,tick);o.demand=AiProductionDemand.ReadState(r,tick,ai);o.launches=AiFactoryLaunchCommitments.ReadState(r,tick);o.expansion.ReadState(r,tick,generation);o.infrastructure.ReadState(r,tick,generation);o.armies=AiArmyRegistry.ReadState(r,ai,difficulty,tick,o.OwnerId,generation);o.armyPlanner.ReadState(r,tick,o.armies);o.defense.ReadState(r,tick,o.armies);o.knowledge=AiKnowledgeTracker.ReadState(r,o.OwnerId,generation,tick,ai);var budgetState=o.budget.Capture();
            AiStateWire.Require(o.budget.OwnerId==o.OwnerId&&o.budget.Generation==generation&&budgetState.AdmittedDecision<=o.ordinal&&
                budgetState.ReservationLifetimeTicks==AiProfile.SecondsToTicks(ai.Value("economy.reservationExpirySeconds"),30),"budget owner binding/allocator/profile horizon");
            o.records.AddRange(WorldWire.Array(r,()=>AiStateWire.ReadPlayableAiTraceRecord(r)));
            AiStateWire.Require(o.records.Count<=128&&o.records.All(rec=>rec!=null&&rec.OwnerId==o.OwnerId&&rec.SourceIdentity==opening.SourceIdentity&&rec.ReceiptIdentity!=null&&rec.ReceiptIdentity.Generation==generation&&rec.ReceiptIdentity.OwnerId==o.OwnerId&&rec.ReceiptIdentity.DecisionOrdinal<=o.ordinal),"owner receipts");
            var scoutOrders=o.records.Where(rec=>rec.Policy=="scout"&&rec.Kind==PlayableCommandKind.Move&&rec.Status==PlayableAiDeliveryStatus.Applied).ToArray();
            AiStateWire.Require(scoutOrders.Length==0||o.lastScoutOrderTick==scoutOrders.Max(rec=>rec.ApplicationTick),"scout order history clock");
            var intel=o.knowledge.Capture();AiStateWire.Require(o.lastScoutTick<=intel.ObservationTick&&(o.lastScoutTick<0||intel.Areas.Any(area=>area.ActuallyCoveredTick>=o.lastScoutTick)),"confirmed survey clock history");
            o.pendingItems.AddRange(WorldWire.Array(r,()=>{
                var p=new Pending{ObservationIdentity=String(r),ObservationTick=r.ReadInt64(),SnapshotSequence=r.ReadInt64(),Action=AiStateWire.ReadPlayableAiAction(r),DueTick=r.ReadInt64(),HumanSequence=r.ReadInt64(),Ordinal=r.ReadInt64(),LocalActionId=r.ReadInt64(),Identity=AiStateWire.ReadAiReceiptIdentity(r),PolicyName=String(r),Scout=Boolean(r)};
                string id=String(r);long expires=r.ReadInt64();int credits=r.ReadInt32(),population=r.ReadInt32();var claims=WorldWire.Array(r,()=>String(r));int priority=r.ReadInt32();double utility=Number(r);string conditions=String(r),reason=String(r);long startup=r.ReadInt64();string startupId=String(r);bool overdue=Boolean(r);
                var a=p.Action;
                AiStateWire.Require(claims.All(c=>!string.IsNullOrWhiteSpace(c))&&claims.Distinct().Count()==claims.Length&&expires>=p.DueTick,"pending reservations");
                AiStateWire.Require(a!=null&&a.ActionId>0&&a.PlayerId==o.OwnerId&&a.Generation==generation&&a.Seed==seed&&a.SourceIdentity==opening.SourceIdentity&&a.ProfileId==profile.ProfileId&&a.ProfileRevision==profile.Revision&&a.SnapshotSequence==p.SnapshotSequence&&p.SnapshotSequence>=0&&p.ObservationTick>=0&&p.ObservationTick<=tick&&p.DueTick>=p.ObservationTick&&p.HumanSequence>=0&&p.Ordinal>0&&p.Ordinal<=o.ordinal&&p.LocalActionId>0&&p.Identity!=null&&p.Identity.Generation==generation&&p.Identity.OwnerId==o.OwnerId&&p.Identity.DecisionOrdinal==p.Ordinal&&p.Identity.ActionOrdinal<=o.actionLimit&&!string.IsNullOrWhiteSpace(p.ObservationIdentity)&&
                    (p.ObservationIdentity=="observation-"+PlayableAiObservation.CurrentSchemaVersion+":"+profile.ProfileId+"@"+profile.Revision+":"+o.OwnerId+":"+generation+":"+p.SnapshotSequence+":"+p.ObservationTick+":"+seed||
                     p.ObservationIdentity=="observation-"+PlayableAiObservation.ParticipantSchemaVersion+":"+profile.ProfileId+"@"+profile.Revision+":"+o.OwnerId+":"+generation+":"+p.SnapshotSequence+":"+p.ObservationTick+":"+seed),"pending binding");
                p.Intent=new AiIntent(id,p.PolicyName,a,expires,credits,population,claims,priority,utility,conditions,reason,startup,startupId,overdue);
                p.Receipt=o.Feedback(p.PolicyName,p.LocalActionId,generation);
                AiStateWire.Require(p.Scout==(p.PolicyName=="scout"),"scout callback");return p;
            }));
            AiStateWire.Require(o.pendingItems.Count<=o.actionLimit&&o.pendingItems.Select(p=>p.PolicyName).Distinct().Count()==o.pendingItems.Count,"pending ledger capacity/policy");
            var obligations=o.budget.Capture().Unpaid.Where(x=>!x.AwaitingPayment).ToArray();
            AiStateWire.Require(obligations.Length==o.pendingItems.Count&&o.pendingItems.All(p=>obligations.Any(x=>x.Id==p.Intent.Id&&x.Receipt.Id==p.Identity.Id&&x.Amount==p.Intent.Credits&&x.Claims.SequenceEqual(p.Intent.Claims))),"pending budget reconciliation");
            foreach(var policy in new[]{"economy","production","research","scout",AiArmyPlanner.Policy,AiDefensePlanner.Policy,"mission-defense","artillery","expansion","infrastructure"})
            {
                long pending=o.PolicyPending(policy);AiStateWire.Require((pending==0)==!o.pendingItems.Any(p=>p.PolicyName==policy),"orphan policy obligation");
            }
            var m=o.mission.Mission;var c=o.midgame.Capture();
            AiStateWire.Require(m==null||m.OwnerId==o.OwnerId&&m.Generation==generation&&m.SourceIdentity==opening.SourceIdentity,"mission binding");
            AiStateWire.Require(c==null||c.OwnerId==o.OwnerId&&c.Generation==generation&&c.Seed==seed&&c.ProfileId==profile.ProfileId&&c.ProfileRevision==profile.Revision&&c.ReviewedTick<=tick&&c.DecisionSequence>=0,"strategy RNG binding");
            var ar=o.artillery.Capture();AiStateWire.Require(ar==null||ar.Seed==seed&&ar.Generation==generation&&ar.ReviewedTick<=tick,"artillery binding");
            o.Checkpoint=o.Capture().BindNativeProfile(ai,o.nativeConfig);return o;
        }
        private long PolicyPending(string name)
        {
            switch(name){case AiDefensePlanner.Policy:return defense.PendingId;case AiArmyPlanner.Policy:return armyPlanner.PendingId;case "infrastructure":return infrastructure.PendingId;case "expansion":return expansion.PendingId;case "economy":return economy.StatePendingId;case "production":return production.StatePendingId;case "research":return research.StatePendingId;case "scout":return scout.StatePendingId;case "mission-defense":return mission.StatePendingId;case "artillery":return artillery.StatePendingId;default:throw new ArgumentException("Unknown pending policy.");}
        }
        private Action<PlayableAiTraceRecord> Feedback(string name,long id,long generation)
        {
            AiStateWire.Require(PolicyPending(name)==id,"terminal callback local ID");
            switch(name){case AiArmyPlanner.Policy:AiStateWire.Require(armyPlanner.PendingGeneration==generation,"callback generation");return armyPlanner.ObserveReceipt;case "infrastructure":AiStateWire.Require(infrastructure.PendingGeneration==generation,"callback generation");return infrastructure.ObserveReceipt;case "expansion":AiStateWire.Require(expansion.PendingGeneration==generation,"callback generation");return expansion.ObserveReceipt;case "economy":AiStateWire.Require(economy.StatePendingGeneration==generation,"callback generation");return economy.ObserveReceipt;
                case "production":AiStateWire.Require(production.StatePendingGeneration==generation,"callback generation");return production.ObserveReceipt;
                case "research":AiStateWire.Require(research.StatePendingGeneration==generation,"callback generation");return research.ObserveReceipt;
                case "scout":AiStateWire.Require(scout.StatePendingGeneration==generation,"callback generation");return scout.ObserveReceipt;
                case AiDefensePlanner.Policy:AiStateWire.Require(defense.PendingGeneration==generation,"callback generation");return defense.ObserveReceipt;
                case "mission-defense":AiStateWire.Require(mission.StatePendingGeneration==generation,"callback generation");return mission.ObserveReceipt;
                case "artillery":AiStateWire.Require(artillery.StatePendingGeneration==generation,"callback generation");return artillery.ObserveReceipt;
                default:throw new ArgumentException("Unknown terminal callback.");}
        }
    }
}
